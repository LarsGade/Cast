# Cast til Windows

En dansk Windows-app til at spejle hele PC'ens skrivebord til en Chromecast på samme Wi-Fi/lokalnetværk. **Ingen Chrome eller anden browser kræves.** Appen finder modtagere med mDNS, styrer dem via Cast V2 og sender en lokal live-videostrøm. FFmpeg er pakket ind i EXE-filen og udpakkes midlertidigt ved brug.

## Download

Hent Windows-appen under [GitHub Releases](https://github.com/LarsGade/Cast/releases). Versionsmærker (`v*`) starter et workflow, som bygger og publicerer `Cast.exe` samt en ZIP med licensoplysninger. En ny download vises først, når workflowet er gennemført.

## Brug

1. Start `Cast.exe` på Windows 10/11 x64.
2. Sæt PC og Chromecast på samme netværk. Vælg enheden i listen. Hvis opdagelse er blokeret, kan du indtaste dens IPv4-adresse fra Google Home.
3. Lad **Del systemlyd** være slået til for at sende lyden fra Windows' standardafspilningsenhed. **Hurtigere billede (30 fps)** er slået til som standard. Klik **Spejl skærmen**. Tillad appen på dit private netværk, hvis Windows-firewall spørger.
4. Klik **Stop**, eller luk appen for at stoppe optagelse og streaming.

Alle tilsluttede skærme optages som ét samlet skrivebord og skaleres til maksimalt 1280×720. Standardprofilen bruger 30 billeder/sekund, en kortere live-playliste og et ønske om afspilning tættere på livebilledet. Slå **Hurtigere billede** fra for den tidligere profil med 20 billeder/sekund og større buffer, hvis billedet hakker. Også mus og notifikationer bliver synlige.

**Systemlyd:** Appen optager via WASAPI-loopback fra Windows' standardafspilningsenhed og sender stereo AAC-lyd sammen med billedet. Mikrofonen optages ikke. Vælg den ønskede højttaler/hovedtelefon i Windows før start; stop og start igen efter skift af lydudgang. Der sendes stilhed, når PC'en ikke afspiller lyd, så videoen stadig kan starte. Slå **Del systemlyd** fra for spejling uden lyd, hvis din lydudgang ikke kan åbnes. Beskyttet lyd/video kan være blokeret.

**Markøren er stadig ikke i realtid.** HLS-afspilningen på Chromecast buffer billedet og lyden, og modtageren bestemmer den endelige forsinkelse. Profilændringerne er en optimering, ikke en måling eller garanti for lavere forsinkelse på alle enheder. Løsningen er ikke beregnet til spil. På en klassisk Chromecast kræver meget lav forsinkelse en anden streamingløsning. En mulig vej er en registreret, hostet Cast Web Receiver med WebRTC; det er ikke implementeret i denne version.

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

Ved udvikling skal `tools/ffmpeg.exe` være hentet via build-scriptet. Tests dækker Cast V2-transport, mDNS-data, audio-mapping, live-playliste og levering af stilhed. `scripts/test-encoder.py` kan desuden verificere H.264/AAC-output med syntetisk video og lyd, når FFmpeg/FFprobe er tilgængelig. De erstatter ikke en fysisk test.

Før release: test på Windows med en fysisk Chromecast, at enheden opdages, video fra skrivebordet vises på TV'et, flere skærme virker, og stop/lukning afslutter billedet. Test også manglende enhed, firewallblokering og frakobling under streaming. Afspil en kendt lyd, kontrollér lyd/billede-synkronisering, og test start under stilhed samt skift af lydudgang. Den tidligere version er rapporteret fungerende til skærmspejling af brugeren; de nye lyd- og profilændringer er endnu ikke afprøvet på fysisk Windows/Chromecast. Fysisk skærmdeling kan ikke verificeres i et Linux-cloudmiljø.

## FFmpeg

Den medfølgende GPL-build bruger libx264. Licenser kopieres til build-mappen; se også [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). Midlertidige optagelsessegmenter slettes ved normal lukning. Efter et nedbrud kan rester i `%TEMP%\Cast` slettes, når appen er lukket.
