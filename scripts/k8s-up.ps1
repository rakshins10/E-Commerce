<#
.SYNOPSIS
    Bring the whole platform up on a local Kubernetes cluster.

.DESCRIPTION
    The Kubernetes equivalent of `docker compose up -d --wait`. It does the five things that have to
    happen in order, and explains each one as it goes:

        1. Build the images (unless -SkipBuild). Kubernetes never builds anything - it runs images
           that already exist. This is the single biggest difference from Compose, where `up` quietly
           builds for you.
        2. Copy the images into the cluster, if this is a kind cluster. A kind node has its own image
           store and cannot see your local one.
        3. Install the HAProxy ingress controller, and wait for it. BEFORE the application, always:
           the controller adopts Ingress objects as it sees them, and one installed afterwards can
           leave routes unclaimed while HAProxy answers 404 to everything.
        4. Apply the application.
        5. Wait for every pod to be ready, then print the URLs.

.PARAMETER SkipBuild
    Use the images already in the local Docker daemon.

.PARAMETER Context
    The kubectl context to deploy into. Defaults to the current one. This script REFUSES to run
    against a context whose name it does not recognise as local - deploying a stack with committed
    dev_only_ passwords into a real cluster is not a mistake worth leaving available.

.EXAMPLE
    ./scripts/k8s-up.ps1
    ./scripts/k8s-up.ps1 -SkipBuild
#>
[CmdletBinding()]
param(
    [switch]$SkipBuild,
    [string]$Context
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

$IMAGES = @(
    'ecommerce-catalog-api', 'ecommerce-basket-api', 'ecommerce-ordering-api',
    'ecommerce-payment-api', 'ecommerce-inventory-api', 'ecommerce-notification-api',
    'ecommerce-user-profile-api', 'ecommerce-back-office-api', 'ecommerce-ordering-saga',
    'ecommerce-storefront-bff', 'ecommerce-admin-bff', 'ecommerce-mobile-bff',
    'ecommerce-react-store', 'ecommerce-angular-store',
    'ecommerce-react-admin', 'ecommerce-angular-admin'
)

function Write-Step($n, $text) { Write-Host "`n[$n] $text" -ForegroundColor Cyan }

# --- Guard: never deploy this into a real cluster --------------------------------------------------
if ($Context) { kubectl config use-context $Context | Out-Null }
$current = (kubectl config current-context).Trim()
if ($current -notmatch '^(kind-|docker-desktop$|minikube$|rancher-desktop$)') {
    throw @"
Refusing to deploy to context '$current'.

This stack ships committed dev_only_ credentials (deploy/k8s/*/secrets.yaml). They are fixtures, not
secrets, and they must never reach a shared cluster. Recognised local contexts are kind-*,
docker-desktop, minikube and rancher-desktop.

If you really mean it, apply the manifests by hand rather than removing this check.
"@
}
Write-Host "Deploying to context: $current" -ForegroundColor Green

# --- 1. Build -------------------------------------------------------------------------------------
if (-not $SkipBuild) {
    Write-Step 1 'Building images (docker compose build)'
    Push-Location (Join-Path $repoRoot 'deploy')
    try { docker compose build; if ($LASTEXITCODE -ne 0) { throw 'docker compose build failed' } }
    finally { Pop-Location }
} else {
    Write-Step 1 'Skipping build (-SkipBuild)'
}

# --- 2. Copy images into a kind node --------------------------------------------------------------
if ($current -like 'kind-*') {
    $clusterName = $current -replace '^kind-', ''
    Write-Step 2 "Loading images into the kind cluster '$clusterName'"
    Write-Host '      A kind node is a container with its own image store; without this every pod'
    Write-Host '      sits in ErrImagePull trying to fetch your local image from Docker Hub.'
    # One invocation for all sixteen: kind reuses the connection, and sixteen separate calls take
    # noticeably longer.
    kind load docker-image --name $clusterName @($IMAGES | ForEach-Object { "${_}:latest" })
    if ($LASTEXITCODE -ne 0) { throw 'kind load failed' }
} else {
    Write-Step 2 'Not a kind cluster - the local image store is shared, nothing to copy'
}

# --- 3. Ingress controller, FIRST -----------------------------------------------------------------
Write-Step 3 'Installing the HAProxy ingress controller'
Write-Host '      Before the application, deliberately: the controller claims Ingress objects as it'
Write-Host '      sees them, and routes created while it is absent can stay unclaimed - which shows'
Write-Host '      up as HAProxy answering 404 to every hostname while `kubectl get ingress` looks'
Write-Host '      perfectly healthy.'
kubectl apply -k (Join-Path $repoRoot 'deploy/k8s/ingress-controller')
if ($LASTEXITCODE -ne 0) { throw 'ingress controller apply failed' }
kubectl rollout status -n haproxy-controller deploy/haproxy-kubernetes-ingress --timeout=300s

# --- 4. The application ---------------------------------------------------------------------------
Write-Step 4 'Applying the application'
Write-Host '      --load-restrictor LoadRestrictionsNone is needed because the Keycloak realm is'
Write-Host '      generated from identity/keycloak/realm-export.json, outside the kustomization'
Write-Host '      directory. `kubectl apply -k` has no such flag, hence the pipe.'
$overlay = Join-Path $repoRoot 'deploy/k8s/overlays/local'
kubectl kustomize --load-restrictor LoadRestrictionsNone $overlay | kubectl apply -f -
if ($LASTEXITCODE -ne 0) { throw 'application apply failed' }

# --- 5. Wait --------------------------------------------------------------------------------------
Write-Step 5 'Waiting for every pod to be ready (up to 10 minutes on a cold start)'
Write-Host '      Keycloak is the slow one: first boot runs Quarkus augmentation and the database'
Write-Host '      migrations together, and everything holding a token waits for it.'
kubectl wait --for=condition=ready pod --all -n ecommerce --timeout=600s
if ($LASTEXITCODE -ne 0) {
    Write-Host "`nNot everything became ready. Start here:" -ForegroundColor Yellow
    Write-Host '  kubectl get pods -n ecommerce | Select-String -NotMatch "1/1"'
    Write-Host '  kubectl describe pod -n ecommerce <name>      # Events, at the bottom'
    Write-Host '  kubectl logs -n ecommerce <name> --previous   # the log of the run that CRASHED'
    Write-Host "`nSee docs/kubernetes/debugging.md."
    throw 'not all pods became ready'
}

Write-Host "`nUp. Every hostname below resolves to 127.0.0.1 with no setup." -ForegroundColor Green
@(
    'React storefront    http://shop.localtest.me',
    'Angular storefront  http://shop-ng.localtest.me',
    'React back office   http://admin.localtest.me',
    'Angular back office http://admin-ng.localtest.me',
    'Storefront BFF      http://api.localtest.me',
    'Admin BFF           http://admin-api.localtest.me',
    'Keycloak            http://auth.localtest.me',
    'Seq (logs)          http://logs.localtest.me',
    'Jaeger (traces)     http://traces.localtest.me',
    'HAProxy stats       http://localhost:1024'
) | ForEach-Object { Write-Host "  $_" }

Write-Host "`nSeed users: customer / support / catalogmgr / ordermgr / administrator, password Passw0rd!"
Write-Host 'Watch traffic move during a deploy: docs/kubernetes/watching-traffic.md'
