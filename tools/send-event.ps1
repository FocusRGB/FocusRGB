<#
.SYNOPSIS
  Sends a test event to SignalRGB's canvas API so an integration effect can react to it.
.EXAMPLE
  .\send-event.ps1 -Sender demo -Event hit
  .\send-event.ps1 demo low
#>
param(
  [Parameter(Position=0)] [string]$Sender = 'demo',
  [Parameter(Position=1)] [string]$Event  = 'hit',
  [int]$Port = 16034
)
$s = [uri]::EscapeDataString($Sender)
$e = [uri]::EscapeDataString($Event)
$url = "http://localhost:$Port/canvas/event?sender=$s&event=$e"
try {
  $r = Invoke-WebRequest -Method Post -Uri $url -UseBasicParsing -TimeoutSec 3
  Write-Host "POST $url -> $($r.StatusCode)"
} catch {
  Write-Host "POST $url failed: $($_.Exception.Message)"
  Write-Host "Is SignalRGB running, and is an effect that implements onCanvasApiEvent selected?"
  exit 1
}
