$root = Split-Path $PSScriptRoot -Parent
Push-Location "$root\frontend\weighing-angular"
npm start
Pop-Location
