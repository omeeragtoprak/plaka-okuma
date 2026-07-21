#!/bin/sh
# AEP proje kontrolü (Stop hook bunu çağırır). Yeşil (exit 0) olmadan "görev tamam" engellenir.
# OtoparkAnpr: tüm-solüsyon derleme (Service/Bench dahil compile kırılmalarını yakalar) + birim testler.
# Not: her stop'ta çalışır (~15-40s). Hızlandırmak istersen sadece `dotnet build`'e indir.
set -e
cd "$(dirname "$0")/.."
dotnet build OtoparkAnpr.sln -c Debug --nologo -clp:ErrorsOnly
dotnet test Anpr.Tests/Anpr.Tests.csproj -c Debug --nologo -v q
