$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$SkillDir = Split-Path -Parent $ScriptDir
$VendorDir = Join-Path $ScriptDir ".vendor\typeless-export-runtime"
$ElectronStubDir = Join-Path $VendorDir "node_modules\electron"

New-Item -ItemType Directory -Force -Path $VendorDir | Out-Null

if (-not (Test-Path (Join-Path $VendorDir "node_modules\electron-store"))) {
    Write-Host "[import-dictionary] Installing electron-store..."
    npm install --prefix $VendorDir --silent --no-fund --no-audit electron-store@10.0.1 | Out-Null
}

New-Item -ItemType Directory -Force -Path $ElectronStubDir | Out-Null

@'
{
  "name": "electron",
  "version": "0.0.0-skill-stub",
  "type": "module",
  "exports": "./index.js"
}
'@ | Set-Content (Join-Path $ElectronStubDir "package.json") -Encoding UTF8

@'
export const app = undefined;
export const ipcMain = undefined;
export const shell = { openPath: async () => '' };
export default { app, ipcMain, shell };
'@ | Set-Content (Join-Path $ElectronStubDir "index.js") -Encoding UTF8

$env:TYPELESS_VENDOR_NODE_MODULES = Join-Path $VendorDir "node_modules"

$DefaultInput = Join-Path $SkillDir "references\typeless-dictionary-export.json"

if ($args -notcontains "--input") {
    node (Join-Path $ScriptDir "import-dictionary.mjs") --input $DefaultInput @args
} else {
    node (Join-Path $ScriptDir "import-dictionary.mjs") @args
}
