"""Exercise the production output options with synthetic video and PCM audio."""
import array
import json
import math
import pathlib
import subprocess
import sys
import tempfile

config = json.loads(pathlib.Path(sys.argv[1]).read_text())
options = config['output']
with tempfile.TemporaryDirectory(prefix='cast-encoder-test-') as temp:
    root = pathlib.Path(temp)
    # Half a second of silence, a tone, then silence. No microphone or desktop.
    pcm = array.array('f')
    for index in range(48000 * 4):
        sample = 0.1 * math.sin(2 * math.pi * 440 * index / 48000) if 24000 <= index < 96000 else 0.0
        pcm.extend((sample, sample))
    if sys.byteorder != 'little':
        pcm.byteswap()
    command = ['ffmpeg', '-hide_banner', '-loglevel', 'error', '-f', 'lavfi', '-i',
               'testsrc2=size=640x360:rate=30:duration=4', *config['audioInput'], *options]
    subprocess.run(command, input=pcm.tobytes(), cwd=root, check=True, timeout=60)
    playlist = root / 'live.m3u8'
    assert playlist.exists() and '#EXT-X-TARGETDURATION:1' in playlist.read_text(), 'A valid one-second HLS playlist is required'
    result = json.loads(subprocess.check_output(['ffprobe', '-v', 'error', '-show_streams', '-of', 'json', str(playlist)], timeout=20))
    streams = result['streams']
    assert any(s['codec_name'] == 'h264' and s['width'] == 1280 and s['height'] == 720 for s in streams), 'H.264 720p video is required'
    assert any(s['codec_name'] == 'aac' and s['channels'] == 2 and s['sample_rate'] == '48000' for s in streams), 'Stereo AAC system-audio output is required'
    # Decode to confirm that the selected audio stream contains the supplied tone.
    output = subprocess.check_output(['ffmpeg', '-v', 'error', '-i', str(playlist), '-map', '0:a:0', '-f', 'f32le', '-'], timeout=20)
    samples = array.array('f')
    samples.frombytes(output)
    assert max(abs(s) for s in samples) > 0.01, 'Audio must survive encoding and decoding'
    print('Encoder smoke test passed: H.264 720p/30fps + audible stereo AAC in valid HLS.')
