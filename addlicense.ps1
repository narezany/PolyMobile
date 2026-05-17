# Requires https://github.com/google/addlicense

Get-ChildItem -Recurse -Filter *.cs |
Where-Object {
    $_.FullName -notlike "*Polytoria\addons\*"
} |
ForEach-Object {
    addlicense -c "Polytoria" -l "MPL-2.0" $_.FullName
}