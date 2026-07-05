#!/usr/bin/env bash
# Verifies the packed nupkg: a fresh consumer with ONLY a PackageReference must
# get the walls (rule fires) and the props flow (CompilerVisibleProperty works).
set -eu; cd "$(dirname "$0")"
rm -rf /tmp/eitri-feed ~/.nuget/packages/eitri.analyzers  # hermetic: kill caches
dotnet pack ../src/Eitri.Analyzers -c Release -o /tmp/eitri-feed -v:q --nologo
rm -rf /tmp/eitri-consumer && mkdir -p /tmp/eitri-consumer && cd /tmp/eitri-consumer
cat > nuget.config << 'NUGET'
<configuration>
  <packageSources><add key="local" value="/tmp/eitri-feed" /></packageSources>
</configuration>
NUGET
cat > Consumer.csproj << 'PROJ'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Eitri_SlicePrefix>Slices.</Eitri_SlicePrefix>
    <Eitri_TokenBudget>15000</Eitri_TokenBudget>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Eitri.Analyzers" Version="0.1.0" PrivateAssets="all" />
  </ItemGroup>
</Project>
PROJ
echo 'namespace Slices.X.Internal; public sealed class Leak { }' > Leak.cs
out=$(dotnet build -v:q --nologo 2>&1) || true
echo "$out" | grep -q "error EIT001" || { echo "PACKAGE TEST FAILED: EIT001"; exit 1; }
rm Leak.cs && echo 'namespace Slices.X.Internal; internal sealed class Ok { }' > Ok.cs
out=$(dotnet build -v:q --nologo -p:Eitri_TokenBudget=10 2>&1) || true
echo "$out" | grep -q "error EIT100" || { echo "PACKAGE TEST FAILED: property flow (EIT100 did not fire at budget=10)"; exit 1; }
echo "package test ok: analyzer bites AND MSBuild properties flow through the packaged props" 
