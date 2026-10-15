"""Write Motion-JPEG AVI with stdlib container code and Pillow dimension reading.
No codec download; the JPEG bitstreams are passed through without alteration.
"""
from pathlib import Path
import struct, argparse, hashlib, json
from PIL import Image

p=argparse.ArgumentParser();p.add_argument('frames');p.add_argument('output');p.add_argument('--fps',type=int,default=15)
a=p.parse_args();files=sorted(Path(a.frames).glob('*.jpg'))
if not files: raise SystemExit('No rendered frames')
source_count=len(files)
width,height=Image.open(files[0]).size
timestamps=Path(a.frames)/'timestamps.csv'
if timestamps.exists():
    rows=[line.split(',') for line in timestamps.read_text().splitlines()]
    times=[float(r[1]) for r in rows];start=times[0];index=0;resampled=[]
    for frame in range(round((times[-1]-start)*a.fps)+1):
        t=start+frame/a.fps
        while index+1<len(times) and times[index+1]<=t:index+=1
        resampled.append(Path(a.frames)/rows[index][0])
    files=resampled
u32=lambda *v:struct.pack('<'+'I'*len(v),*v)
def chunk(tag,payload):return tag+u32(len(payload))+payload+(b'\0' if len(payload)%2 else b'')
def listchunk(kind,payload):return chunk(b'LIST',kind+payload)
count=len(files);max_frame=max(f.stat().st_size for f in files)
avih=u32(1000000//a.fps,max_frame*a.fps,0,0x10,count,0,1,max_frame,width,height,0,0,0,0)
strh=struct.pack('<4s4sIHHIIIIIIIIhhhh',b'vids',b'MJPG',0,0,0,0,1,a.fps,0,count,max_frame,0xffffffff,0,0,0,width,height)
strf=struct.pack('<IiiHH4sIiiII',40,width,height,1,24,b'MJPG',width*height*3,0,0,0,0)
header=listchunk(b'hdrl',chunk(b'avih',avih)+listchunk(b'strl',chunk(b'strh',strh)+chunk(b'strf',strf)))
out=Path(a.output);out.parent.mkdir(parents=True,exist_ok=True)
with out.open('wb') as stream:
    stream.write(b'RIFF\0\0\0\0AVI '+header)
    movi_start=stream.tell();stream.write(b'LIST\0\0\0\0movi');index=[]
    for f in files:
        data=f.read_bytes();offset=stream.tell()-(movi_start+8)
        stream.write(chunk(b'00dc',data));index.append(struct.pack('<4sIII',b'00dc',0x10,offset,len(data)))
    movi_end=stream.tell();stream.seek(movi_start+4);stream.write(u32(movi_end-movi_start-8));stream.seek(movi_end)
    stream.write(chunk(b'idx1',b''.join(index)));end=stream.tell();stream.seek(4);stream.write(u32(end-8))
with out.open('rb') as encoded:
    encoded_hash=hashlib.file_digest(encoded,'sha256').hexdigest()
run_header=Path(a.frames).parent/'header.json'
metadata={'schema':'showcase-rendered-video/1','output':str(out.resolve()),'sha256':encoded_hash,'bytes':out.stat().st_size,
    'width':width,'height':height,'sourceCapturedFrames':source_count,'outputFrames':count,'playbackFps':a.fps,'durationSeconds':count/a.fps,
    'sourceDirectory':str(Path(a.frames).resolve()),'encoding':'Original JPEG bitstreams wrapped as MJPEG AVI; timestamp-based hold-last-frame resampling, no interpolated imagery.',
    'measurement':'Playback FPS is not the application render FPS. Capture cost is excluded by using separate clean benchmark runs.',
    'timestampsSha256':hashlib.sha256(timestamps.read_bytes()).hexdigest() if timestamps.exists() else None,
    'runHeaderSha256':hashlib.sha256(run_header.read_bytes()).hexdigest() if run_header.exists() else None,
    'runHeader':json.loads(run_header.read_text(encoding='utf-8-sig')) if run_header.exists() else None}
out.with_suffix('.json').write_text(json.dumps(metadata,ensure_ascii=False,indent=2),encoding='utf-8')
print(f'{out}: {source_count} captured frames, {count} output frames, {width}x{height}, playback {a.fps}fps, {count/a.fps:.1f}s')
