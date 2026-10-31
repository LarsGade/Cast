# Cast til Windows

En dansk Windows-app til at spejle hele PC'ens skrivebord til en Chromecast på samme Wi-Fi/lokalnetværk. **Ingen Chrome eller anden browser kræves.** Appen finder modtagere med mDNS, styrer dem via Cast V2 og sender en lokal live-videostrøm. FFmpeg er pakket ind i EXE-filen og udpakkes midlertidigt ved brug.

## Download

Hent Windows-appen under [GitHub Releases](https://github.com/LarsGade/Cast/releases). Versionsmærker (`v*`) starter et workflow, som bygger og publicerer `Cast.exe` samt en ZIP med licensoplysninger. En ny download vises først, når workflowet er gennemført.

## Brug

1. Start `Cast.exe` på Windows 10/11 x64.
2. Sæt PC og Chromecast på samme netværk. Vælg enheden i listen. Hvis opdagelse er blokeret, kan du indtaste dens IPv4-adresse fra Google Home.
3. Klik **Spejl skærmen**. Tillad appen på dit private netværk, hvis Windows-firewall spørger.
4. Klik **Stop**, eller luk appen for at stoppe optagelse og streaming.

Alle tilsluttede skærme optages som ét samlet skrivebord og skaleres til maksimalt 1280×720 med 20 billeder/sekund. Også mus og notifikationer bliver synlige. Denne version sender **ikke systemlyd**. HLS-streaming har flere sekunders forsinkelse og egner sig til præsentationer; den er ikke beregnet til spil. Beskyttet indhold kan blive sort.

Samme Wi-Fi skal tillade trafik mellem enhederne: gæstenetværk, klientisolering, VPN og firewall kan blokere. Appen bruger mDNS UDP 5353, Cast TCP 8009 og en dynamisk lokal HTTP-port, som Chromecast henter video fra. Der kræves ikke internet til appens kode, men Chromecast kan kræve internet til at indlæse Googles standardmodtager.

Videoen leveres over lokal HTTP med en tilfældig URL pr. session; brug et betroet privat netværk. Cast-enheder anvender selvsignerede TLS-certifikater. Appen understøtter ikke Googles særskilte device-auth-verifikation. Appen kan erstatte eksisterende afspilning på den valgte Chromecast.

## Byg EXE

Installer [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), åbn PowerShell i projektmappen og kør:

```powershell
./build.ps1
```

Scriptet henter FFmpeg fra BtbN's officielle GitHub-releases, verificerer SHA-256 mod udgiverens manifest, kører protokoltests og bygger **`artifacts/win-x64/Cast.exe`** med indbygget .NET-runtime og FFmpeg. Det kræver internet under første build. Windows-brugeren skal hverken installere .NET, FFmpeg eller Chrome. EXE-filen er ikke kodesigneret.

GitHub Actions bruger den tilsvarende Linux-krydsbygning i `scripts/setup-cloud.sh` ved push/PR og manuelt. En vellykket kørsel udgiver artifact **Cast-windows-x64**. Ved push af et versionsmærke (`v*`) publicerer workflowet også en GitHub-testrelease med EXE og ZIP.

## Udvikling og tests

```powershell
dotnet run --project src/Cast
dotnet run --project tests/Cast.ProtocolTests -c Release
```

Ved udvikling skal `tools/ffmpeg.exe` være hentet via build-scriptet. Protokoltests dækker Cast V2 protobuf-pakker, fejlbehæftede pakker og mDNS-data. De erstatter ikke en fysisk test.

Før release: test på Windows med en fysisk Chromecast, at enheden opdages, video fra skrivebordet vises på TV'et, flere skærme virker, og stop/lukning afslutter billedet. Test også manglende enhed, firewallblokering og frakobling under streaming. Fysisk skærmdeling kan ikke verificeres i et Linux-cloudmiljø.

## FFmpeg

Den medfølgende GPL-build bruger libx264. Licenser kopieres til build-mappen; se også [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). Midlertidige optagelsessegmenter slettes ved normal lukning. Efter et nedbrud kan rester i `%TEMP%\Cast` slettes, når appen er lukket.
