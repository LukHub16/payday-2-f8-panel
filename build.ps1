$ErrorActionPreference = 'Stop'
$package = Join-Path $PSScriptRoot '..\payday-overlay-sdk\package'
if (-not (Test-Path -LiteralPath $package)) {
    $package = Join-Path $PSScriptRoot 'sdk'
    if (-not (Test-Path -LiteralPath $package)) {
        Expand-Archive -LiteralPath (Join-Path $PSScriptRoot 'webview2.zip') -DestinationPath $package
    }
}
$output = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Path $output -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $package 'lib\net462\Microsoft.Web.WebView2.Core.dll') -Destination $output
Copy-Item -LiteralPath (Join-Path $package 'lib\net462\Microsoft.Web.WebView2.WinForms.dll') -Destination $output
Copy-Item -LiteralPath (Join-Path $package 'runtimes\win-x64\native\WebView2Loader.dll') -Destination $output
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:winexe /platform:x64 /optimize+ "/out:$output\PaydayPanel.exe" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll "/r:$output\Microsoft.Web.WebView2.Core.dll" "/r:$output\Microsoft.Web.WebView2.WinForms.dll" (Join-Path $PSScriptRoot 'Panel.cs')
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
Write-Output "Built: $output\PaydayPanel.exe"
