Retter den genskabte opstartsfejl **Error opening input files: Invalid argument** i v0.2.0.

Årsagen var `-thread_queue_size` før skærm-/lydinput. I den medfølgende nye FFmpeg-build er det en output-/muxerindstilling, så processen afviste inputargumenterne. Flaget er nu fjernet fra begge inputs. Den tidligere Linux-test brugte en ældre FFmpeg-version og fangede ikke fejlen.

- Skærmoptagelsen finder nu hele skrivebordets område i FFmpegs egen DPI-kontekst. Der sendes ikke længere et manuelt udsnit baseret på appens skærmkoordinater. Det undgår en kilde til ugyldige koordinater ved flere skærme og forskellig skalering.
- **Vis fejllog → Kopiér log** viser hele FFmpeg-fejlen, også efter at opstarten er afbrudt. Hvis opstart stadig fejler, medsend denne tekst.
- Versionsnummeret vises i vinduets titel.
- Windows-workflowet tester nu både den medfølgende FFmpeg-eksekverbar med de præcise lydinputparametre og reel gdigrab-skrivebordsoptagelse før en release. Linux-tests erstatter dermed ikke længere Windows-kodertesten.

Download `Cast.exe`, luk den tidligere version og åbn den nye. Systemlyd og hurtigere videoprofil er fortsat tilgængelige uden Chrome eller ekstra opsætning på Chromecast. Markøren er fortsat forsinket af HLS/Chromecast-afspilningen.

Opstartsfejlen er genskabt med en matchende FFmpeg-build. Den rettede proces testes med de samme inputparametre og den medfølgende Windows-eksekverbar. Hvis opstart stadig fejler på en konkret PC, kan den fulde fejllog nu kopieres. Fysisk optagelse af systemlyd og afspilning på brugerens Chromecast kan ikke valideres i cloudmiljøet.
