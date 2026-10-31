Tilføjer **systemlyd** og en hurtigere videoprofil til den browseruafhængige Windows-app.

- **Del systemlyd** er slået til som standard. Lyden optages fra Windows' standardhøjttaler/hovedtelefon og sendes som stereo AAC. Ingen mikrofonoptagelse.
- **Hurtigere billede (30 fps)** er slået til som standard: kortere live-playliste og mindre encoderbuffer. Slå det fra, hvis forbindelsen hakker.
- Start fungerer også, når PC'en er stille. Stop og start spejlingen igen, hvis du skifter lydudgang i Windows.

**Markøren er ikke i realtid.** Chromecastens standardmodtager buffer fortsat video og lyd. Den faktiske forsinkelse og ændringen heraf er ikke målt på fysisk hardware. Meget lav forsinkelse på klassisk Chromecast kræver en anden streamingløsning og er ikke løst i denne version.

Download `Cast.exe` på Windows 10/11 x64. ZIP-filen indeholder også tredjepartslicenser. Ingen Chrome, .NET eller lyd-driver skal installeres særskilt. Tillad appen på dit private netværk i Windows-firewall.

Validering: 17 automatiske checks og en encoder-smoketest med H.264-video og hørbar stereo AAC efter encode/decode. Den tidligere skærmspejling er rapporteret fungerende af brugeren; nye WASAPI-lydoptagelser og den ændrede videoprofil mangler fysisk Windows/Chromecast-test.
