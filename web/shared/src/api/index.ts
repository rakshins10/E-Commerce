/**
 * The HTTP client every application uses to reach its BFF.
 *
 * Framework-neutral on purpose: it takes a function that supplies the current
 * access token rather than reaching into a React hook or an Angular service.
 * That one decision is what lets React, Angular and React Native share it.
 *
 * @see docs/adr/0006-yarp-gateway-and-bff-per-client.md
 */

/** The error contract every service returns — RFC 9457 Problem Details. */
export interface ProblemDetails {
  readonly type?: string;
  readonly title: string;
  readonly status: number;
  readonly detail?: string;
  readonly instance?: string;
  /** Field-level validation failures, keyed by property name. */
  readonly errors?: Readonly<Record<string, readonly string[]>>;
  /** Our correlation id, echoed back so a user can quote it to support. */
  readonly correlationId?: string;
}

/**
 * A failed API call.
 *
 * Carries the status and the parsed problem details so a caller can branch on
 * `status === 403` to show a permission message, or read `errors` to attach
 * messages to form fields.
 */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly problem: ProblemDetails,
    readonly correlationId?: string,
  ) {
    super(problem.detail ?? problem.title ?? `Request failed with status ${status}`);
    this.name = 'ApiError';
  }

  /** Not signed in, or the token expired. The caller should re-authenticate. */
  get isUnauthenticated(): boolean {
    return this.status === 401;
  }

  /** Signed in, but lacking the permission. Show a 403 view — do not retry. */
  get isForbidden(): boolean {
    return this.status === 403;
  }

  get isNotFound(): boolean {
    return this.status === 404;
  }

  /** 400 or 422 — the request itself was rejected, so retrying is pointless. */
  get isValidationFailure(): boolean {
    return this.status === 400 || this.status === 422;
  }
}

export interface ApiClientOptions {
  readonly baseUrl: string;
  /**
   * Supplies the current access token, or null when signed out.
   *
   * A function rather than a value because tokens expire and rotate — capturing
   * one at construction time would attach a stale token to every later request.
   */
  readonly getAccessToken: () => string | null | Promise<string | null>;
  /** Called on a 401 so the app can trigger a silent renew or redirect to login. */
  readonly onUnauthenticated?: () => void;
}

export interface RequestOptions {
  readonly signal?: AbortSignal;
  readonly query?: Readonly<Record<string, string | number | boolean | undefined>>;
  readonly headers?: Readonly<Record<string, string>>;
}

/** Generates the correlation id sent on every request. */
function newCorrelationId(): string {
  return typeof crypto !== 'undefined' && 'randomUUID' in crypto
    ? crypto.randomUUID()
    : `web-${Date.now()}-${Math.random().toString(16).slice(2)}`;
}

/**
 * A small typed fetch wrapper.
 *
 * Deliberately not a generated client yet — from Phase 4 the request/response
 * types come from each BFF's OpenAPI document, so a backend contract change
 * breaks the frontend *build* rather than producing a runtime `undefined`.
 * This class remains the transport underneath.
 */
export class ApiClient {
  constructor(private readonly options: ApiClientOptions) {}

  get<T>(path: string, options?: RequestOptions): Promise<T> {
    return this.send<T>('GET', path, undefined, options);
  }

  post<T>(path: string, body?: unknown, options?: RequestOptions): Promise<T> {
    return this.send<T>('POST', path, body, options);
  }

  put<T>(path: string, body?: unknown, options?: RequestOptions): Promise<T> {
    return this.send<T>('PUT', path, body, options);
  }

  patch<T>(path: string, body?: unknown, options?: RequestOptions): Promise<T> {
    return this.send<T>('PATCH', path, body, options);
  }

  delete<T>(path: string, options?: RequestOptions): Promise<T> {
    return this.send<T>('DELETE', path, undefined, options);
  }

  private async send<T>(
    method: string,
    path: string,
    body?: unknown,
    options?: RequestOptions,
  ): Promise<T> {
    const url = new URL(path.replace(/^\//, ''), `${this.options.baseUrl.replace(/\/$/, '')}/`);

    for (const [key, value] of Object.entries(options?.query ?? {})) {
      if (value !== undefined) url.searchParams.set(key, String(value));
    }

    const token = await this.options.getAccessToken();

    const headers: Record<string, string> = {
      Accept: 'application/json',
      // Sent on EVERY request and propagated by the server across every service
      // and every async hop, so one id finds the whole story in Seq.
      // See docs/operations/observability.md.
      'X-Correlation-Id': newCorrelationId(),
      ...options?.headers,
    };

    if (body !== undefined) headers['Content-Type'] = 'application/json';
    if (token) headers.Authorization = `Bearer ${token}`;

    const response = await fetch(url, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
      signal: options?.signal,
    });

    if (response.status === 401) {
      this.options.onUnauthenticated?.();
    }

    if (!response.ok) {
      throw new ApiError(
        response.status,
        await this.readProblem(response),
        response.headers.get('X-Correlation-Id') ?? undefined,
      );
    }

    if (response.status === 204) return undefined as T;

    const text = await response.text();
    return (text ? JSON.parse(text) : undefined) as T;
  }

  private async readProblem(response: Response): Promise<ProblemDetails> {
    try {
      const problem = (await response.json()) as ProblemDetails;
      if (problem && typeof problem === 'object' && 'title' in problem) return problem;
    } catch {
      // Not every failure returns Problem Details - a gateway timeout or a
      // proxy error may return HTML or nothing at all. Fall through.
    }

    return { title: response.statusText || 'Request failed', status: response.status };
  }
}
