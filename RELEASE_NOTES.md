Opdatering af opstart og fejldiagnostik efter fejlen **Error opening input files: Invalid argument**.

- Skærmoptagelsen finder nu hele skrivebordets område i FFmpegs egen DPI-kontekst. Der sendes ikke længere et manuelt udsnit baseret på appens skærmkoordinater. Det undgår en kilde til ugyldige koordinater ved flere skærme og forskellig skalering.
- **Vis fejllog → Kopiér log** viser hele FFmpeg-fejlen, også efter at opstarten er afbrudt. Hvis opstart stadig fejler, medsend denne tekst.
- Versionsnummeret vises i vinduets titel.
- Windows-workflowet tester nu både den medfølgende FFmpeg-eksekverbar med de præcise lydinputparametre og reel gdigrab-skrivebordsoptagelse før en release. Linux-tests erstatter dermed ikke længere Windows-kodertesten.

Download `Cast.exe`, luk den tidligere version og åbn den nye. Systemlyd og hurtigere videoprofil er fortsat tilgængelige uden Chrome eller ekstra opsætning på Chromecast. Markøren er fortsat forsinket af HLS/Chromecast-afspilningen.

Det er ikke bekræftet, at brugerens konkrete opstartsfejl er løst; den tidligere version skjulte årsagen, og de nye logs gør en præcis diagnose mulig, hvis den fortsætter. Fysisk optagelse af systemlyd og afspilning på brugerens Chromecast kan ikke valideres i cloudmiljøet.
