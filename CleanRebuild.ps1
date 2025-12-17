# CleanRebuild.ps1 - Force clean rebuild to update cached HTML files
# Run this from the solution root directory

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  FXOAiTranslate - Clean Rebuild" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Clean the solution
Write-Host "[1/4] Cleaning solution..." -ForegroundColor Yellow
dotnet clean FXOAiTranslate.sln
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: dotnet clean failed!" -ForegroundColor Red
    exit 1
}
Write-Host "✓ Solution cleaned" -ForegroundColor Green
Write-Host ""

# Step 2: Delete bin and obj folders to ensure fresh start
Write-Host "[2/4] Deleting bin and obj folders..." -ForegroundColor Yellow

$foldersToDelete = @(
    ".\FXOAiTranslate\bin",
    ".\FXOAiTranslate\obj",
    ".\FXOAiTranslate.WPF\bin",
    ".\FXOAiTranslate.WPF\obj",
    ".\FXOptionsSimulator\bin",
    ".\FXOptionsSimulator\obj"
)

foreach ($folder in $foldersToDelete) {
    if (Test-Path $folder) {
        Remove-Item -Recurse -Force $folder -ErrorAction SilentlyContinue
        Write-Host "  ✓ Deleted: $folder" -ForegroundColor Gray
    } else {
        Write-Host "  - Skipped: $folder (not found)" -ForegroundColor DarkGray
    }
}
Write-Host "✓ Build artifacts deleted" -ForegroundColor Green
Write-Host ""

# Step 3: Rebuild solution
Write-Host "[3/4] Rebuilding solution (no incremental)..." -ForegroundColor Yellow
dotnet build FXOAiTranslate.sln --no-incremental
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: dotnet build failed!" -ForegroundColor Red
    exit 1
}
Write-Host "✓ Solution rebuilt successfully" -ForegroundColor Green
Write-Host ""

# Step 4: Verify HTML file was copied
Write-Host "[4/4] Verifying HTML file deployment..." -ForegroundColor Yellow

$htmlPath = ".\FXOAiTranslate.WPF\bin\Debug\net8.0-windows\Documentation\fx_aggregator_fixed_380.html"

if (Test-Path $htmlPath) {
    $fileInfo = Get-Item $htmlPath
    $fileSize = [math]::Round($fileInfo.Length / 1KB, 2)
    $timestamp = $fileInfo.LastWriteTime

    Write-Host "  ✓ HTML file found: $htmlPath" -ForegroundColor Gray
    Write-Host "  ✓ Size: $fileSize KB" -ForegroundColor Gray
    Write-Host "  ✓ Last modified: $timestamp" -ForegroundColor Gray

    # Check for old hardcoded LP names (should NOT exist)
    $content = Get-Content $htmlPath -Raw
    $hasOldLPs = $content -match "DEUT|BARX|(?<!-)JPM(?!\.)"

    if ($hasOldLPs) {
        Write-Host ""
        Write-Host "⚠ WARNING: HTML still contains old hardcoded LP names (DEUT, JPM, CITI, BARX)" -ForegroundColor Red
        Write-Host "  This suggests the HTML file wasn't updated correctly." -ForegroundColor Red
    } else {
        # Check for new dynamic code
        $hasDynamicCode = $content -match "buildDynamicLPLadder"

        if ($hasDynamicCode) {
            Write-Host ""
            Write-Host "✓ HTML file is CORRECT - has dynamic LP loading!" -ForegroundColor Green
            Write-Host "  Expected: 9 LPs (SOCGEN, CIBC, MS, HSBC, NATWEST, SCBL, NOMURA, BAML, BNP)" -ForegroundColor Green
        } else {
            Write-Host ""
            Write-Host "⚠ WARNING: buildDynamicLPLadder function not found!" -ForegroundColor Red
        }
    }
} else {
    Write-Host "  ✗ HTML file NOT found at expected location!" -ForegroundColor Red
    Write-Host "  Expected: $htmlPath" -ForegroundColor Red
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Rebuild Complete!" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Run the application" -ForegroundColor White
Write-Host "  2. Right-click a trade → 'Send to GFI Fenics'" -ForegroundColor White
Write-Host "  3. You should see 9 LPs dynamically loaded" -ForegroundColor White
Write-Host ""
