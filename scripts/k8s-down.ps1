<#
.SYNOPSIS
    Remove the platform from a local Kubernetes cluster.

.DESCRIPTION
    The Kubernetes equivalent of `docker compose down`, with the same distinction between "stop it"
    and "destroy the data" that `-v` makes there.

    By default this deletes the whole namespace, which takes the PersistentVolumeClaims with it - so
    every database is emptied. That is usually what you want locally, and it is worth being explicit
    about, because a claim quietly deleted along with its namespace is how people lose data they
    meant to keep.

.PARAMETER KeepData
    Delete the workloads but leave the PersistentVolumeClaims. The next `k8s-up.ps1` reattaches to
    the same databases, so seed data, orders and Keycloak's realm survive.

.PARAMETER RemoveController
    Also remove the HAProxy ingress controller. Off by default: it is cluster infrastructure that
    other things may be using, and reinstalling it costs a minute.

.PARAMETER DeleteCluster
    Delete the whole kind cluster. The bluntest and fastest reset; nothing survives.
#>
[CmdletBinding()]
param(
    [switch]$KeepData,
    [switch]$RemoveController,
    [switch]$DeleteCluster
)

$ErrorActionPreference = 'Stop'
$current = (kubectl config current-context).Trim()

if ($DeleteCluster) {
    if ($current -notlike 'kind-*') { throw "Context '$current' is not a kind cluster; -DeleteCluster only applies to kind." }
    $name = $current -replace '^kind-', ''
    Write-Host "Deleting the entire kind cluster '$name'." -ForegroundColor Yellow
    kind delete cluster --name $name
    return
}

if ($KeepData) {
    Write-Host "Removing workloads from '$current', keeping the data." -ForegroundColor Cyan
    Write-Host 'The namespace stays, because deleting it would take the PersistentVolumeClaims with it.'
    Write-Host ''
    Write-Host 'Note that deleting a StatefulSet does NOT delete its claims - that asymmetry is'
    Write-Host 'deliberate in Kubernetes, and it is what makes this option possible at all: a database'
    Write-Host 'pod is disposable, the disk under it is not.'
    kubectl delete deployment,statefulset,service,ingress,configmap,secret `
        -n ecommerce -l app.kubernetes.io/part-of=ecommerce --ignore-not-found
    Write-Host "`nSurviving claims:" -ForegroundColor Green
    kubectl get pvc -n ecommerce
} else {
    Write-Host "Removing the ecommerce namespace from '$current'." -ForegroundColor Cyan
    Write-Host 'This deletes every workload in it AND every PersistentVolumeClaim - all database'
    Write-Host 'contents are gone. Use -KeepData to keep them.'
    Write-Host ''
    Write-Host 'Deleting a namespace is not instant: Kubernetes finalises each object inside it, so it'
    Write-Host 'can sit in Terminating for a minute. A namespace stuck there for much longer usually'
    Write-Host 'means a resource with a finalizer nobody is left to satisfy.'
    kubectl delete namespace ecommerce --ignore-not-found --wait
}

if ($RemoveController) {
    Write-Host "`nRemoving the HAProxy ingress controller."
    kubectl delete namespace haproxy-controller --ignore-not-found --wait
    kubectl delete ingressclass haproxy --ignore-not-found
}

Write-Host "`nDone." -ForegroundColor Green
kubectl get all -n ecommerce 2>$null
