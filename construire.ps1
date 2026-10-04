# Reconstruit les atlas (si Python est disponible) puis compile une mascotte par dossier de personnages\.
# Usage : clic droit > Executer avec PowerShell, ou   powershell -File construire.ps1 [nom ...]
# (sans nom : toutes les mascottes ; avec des noms de dossiers, par exemple jungkook : celles-la seulement)
# Une mascotte en cours d'execution verrouille son exe : la quitter avant de recompiler.
param([string[]]$Noms)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

if (Get-Command python -ErrorAction SilentlyContinue) {
    python outils\construire_atlas.py @Noms
    if ($LASTEXITCODE -ne 0) { throw "La construction des atlas a echoue." }
}

$net = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
foreach ($dossier in Get-ChildItem personnages -Directory) {
    if ($Noms -and $Noms -notcontains $dossier.Name) { continue }
    $fiche = Join-Path $dossier.FullName 'perso.txt'
    $lignes = Get-Content $fiche -Encoding UTF8
    $id = ($lignes | Where-Object { $_ -like 'id=*' } | Select-Object -First 1).Substring(3).Trim()
    if ($id -eq 'Claude') { $atlas = 'assets\claude-atlas.png'; $icone = 'assets\icone.ico' }
    else { $atlas = Join-Path $dossier.FullName 'atlas.png'; $icone = Join-Path $dossier.FullName 'icone.ico' }
    if (-not (Test-Path $atlas)) { Write-Host "Mascotte$id : pas encore d'images, ignoree"; continue }

    # ressources facultatives : autres formes du personnage (atlas2, atlas3), tuiles du bloc (objets),
    # coeurs et notes de musique (particules)
    $extras = @(foreach ($nom in 'atlas2.png', 'atlas3.png', 'objets.png', 'particules.png') {
        $chemin = Join-Path $dossier.FullName $nom
        if (Test-Path $chemin) { "/resource:$chemin,$nom" }
    })
    # tenues : tenue1.png, tenue2.png... (voir tenues= dans la fiche)
    $extras += foreach ($t in Get-ChildItem $dossier.FullName -Filter 'tenue*.png') { "/resource:$($t.FullName),$($t.Name)" }

    # l'exe va a la racine du projet, sauf si la fiche lui donne un autre dossier (sortie=)
    $exe = "Mascotte$id.exe"
    $sortie = $lignes | Where-Object { $_ -like 'sortie=*' } | Select-Object -First 1
    if ($sortie) {
        $dossierExe = Join-Path $PSScriptRoot $sortie.Substring(7).Trim()
        New-Item -ItemType Directory -Force $dossierExe | Out-Null
        $exe = Join-Path (Resolve-Path $dossierExe) "Mascotte$id.exe"
    }

    & "$net\csc.exe" /nologo /target:winexe /optimize+ /codepage:65001 `
        "/out:$exe" "/win32icon:$icone" "/resource:$atlas,atlas.png" "/resource:$fiche,perso.txt" $extras `
        /r:"$net\WPF\PresentationFramework.dll" /r:"$net\WPF\PresentationCore.dll" /r:"$net\WPF\WindowsBase.dll" /r:System.Xaml.dll `
        src\Mascotte.cs
    if ($LASTEXITCODE -ne 0) { throw "La compilation de Mascotte$id a echoue." }
    Write-Host "OK : $exe"
}
