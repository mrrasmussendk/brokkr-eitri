#!/usr/bin/env bash
# Verifies the packed nupkg: a fresh consumer with ONLY a PackageReference must
# get the walls (rule fires) and the props flow (CompilerVisibleProperty works).
set -eu; cd "$(dirname "$0")"
rm -rf /tmp/garmr-feed ~/.nuget/packages/garmr.analyzers  # hermetic: kill caches
dotnet pack ../src/Garmr.Analyzers -c Release -o /tmp/garmr-feed -v:q --nologo
rm -rf /tmp/garmr-consumer && mkdir -p /tmp/garmr-consumer && cd /tmp/garmr-consumer
cat > nuget.config << 'NUGET'
<configuration>
  <packageSources><add key="local" value="/tmp/garmr-feed" /></packageSources>
</configuration>
NUGET
cat > Consumer.csproj << 'PROJ'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Garmr_SlicePrefix>Slices.</Garmr_SlicePrefix>
    <Garmr_TokenBudget>15000</Garmr_TokenBudget>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Garmr.Analyzers" Version="0.1.0" PrivateAssets="all" />
  </ItemGroup>
</Project>
PROJ
echo 'namespace Slices.X.Internal; public sealed class Leak { }' > Leak.cs
out=$(dotnet build -v:q --nologo 2>&1) || true
echo "$out" | grep -q "error GARM001" || { echo "PACKAGE TEST FAILED: GARM001"; exit 1; }
rm Leak.cs && echo 'namespace Slices.X.Internal; internal sealed class Ok { }' > Ok.cs
out=$(dotnet build -v:q --nologo -p:Garmr_TokenBudget=10 2>&1) || true
echo "$out" | grep -q "error GARM100" || { echo "PACKAGE TEST FAILED: property flow (GARM100 did not fire at budget=10)"; exit 1; }
echo "package test ok: analyzer bites AND MSBuild properties flow through the packaged props" 
