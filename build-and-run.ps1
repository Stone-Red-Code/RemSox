Set-Location $PSScriptRoot

cosmos build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

cosmos run