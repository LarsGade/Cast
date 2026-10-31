# FFmpeg

Cast bundles an unmodified FFmpeg executable from https://github.com/BtbN/FFmpeg-Builds (win64-gpl). FFmpeg is free software distributed under the GNU General Public License for this build, including libx264. FFmpeg is executed as a separate process.

Build recipes, patches and upstream source references: https://github.com/BtbN/FFmpeg-Builds . FFmpeg source: https://git.ffmpeg.org/ffmpeg.git . x264 source: https://code.videolan.org/videolan/x264 . GPL text: https://www.gnu.org/licenses/gpl-3.0.html . The downloaded archive includes the applicable license files, which build.ps1 copies alongside the executable.

The downloaded checksums.sha256 manifest is retained under tools/ during development to identify the exact binary. When redistributing binaries, retain the relevant license notices and provide corresponding source as required by the FFmpeg build's licenses.
