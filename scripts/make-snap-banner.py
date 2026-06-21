#!/usr/bin/env python3
# Generates the 720x240 Snap Store banner (docs/snap-banner-720x240.png) using the app's
# ocean-blue/teal palette + the real app icon. Drawn with Pillow (no SVG rasterizer needed).
#   python3 scripts/make-snap-banner.py            # writes docs/snap-banner-720x240.png
#   OUT=/tmp/x.png python3 scripts/make-snap-banner.py
from PIL import Image, ImageDraw, ImageFont
import os

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.environ.get("OUT", os.path.join(REPO,"docs","snap-banner-720x240.png"))
W, H = 720, 240
S = 3
w, h = W*S, H*S
FB = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"
FR = "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"

def lerp(a,b,t): return tuple(int(a[i]+(b[i]-a[i])*t) for i in range(3))

# diagonal 3-stop teal gradient
c0,c1,c2 = (0x0B,0x3B,0x4A),(0x0E,0x8F,0xB3),(0x2D,0xBE,0xD6)
gw,gh = 360,120
grad = Image.new("RGB",(gw,gh)); gp = grad.load()
for y in range(gh):
    for x in range(gw):
        t=(x/(gw-1)+y/(gh-1))/2
        gp[x,y]=lerp(c0,c1,t/0.5) if t<0.5 else lerp(c1,c2,(t-0.5)/0.5)
img = Image.new("RGBA",(w,h),(0,0,0,0))
img.paste(grad.resize((w,h),Image.LANCZOS).convert("RGBA"),(0,0))
d = ImageDraw.Draw(img)

# faint motion streaks confined to the empty top-right corner only
for i,(yy,x1) in enumerate([(30,600),(46,628),(62,656)]):
    d.line([(x1*S,yy*S),(702*S,yy*S)], fill=(255,255,255,26), width=6*S)

# logo: soft disc + real app icon
cx,cy,r = 120,120,86
d.ellipse([(cx-r)*S,(cy-r)*S,(cx+r)*S,(cy+r)*S], fill=(255,255,255,30))
icon = Image.open(REPO+"/src/Downloader.Desktop/Assets/downloader512.png").convert("RGBA")
isz=132*S; icon=icon.resize((isz,isz),Image.LANCZOS)
img.paste(icon,(cx*S-isz//2,cy*S-isz//2),icon)

# wordmark + tagline
fbig=ImageFont.truetype(FB,60*S); ftag=ImageFont.truetype(FR,23*S); ffeat=ImageFont.truetype(FB,14*S)
tx=232*S
d.text((tx,54*S),"Downloader",font=fbig,fill=(255,255,255,255))
d.text((tx+3*S,130*S),"Fast multi-connection download manager",font=ftag,fill=(235,248,252,245))

# feature pills, auto-fit (never clipped)
px=tx+2*S; py=180*S; right_max=705*S
for f in ["MULTIPART","PAUSE / RESUME","QUEUE","SCHEDULER"]:
    bb=d.textbbox((0,0),f,font=ffeat); tw=bb[2]-bb[0]; th=bb[3]-bb[1]
    padx,pady=10*S,7*S
    if px+tw+2*padx > right_max: break
    d.rounded_rectangle([px,py,px+tw+2*padx,py+th+2*pady],radius=15*S,fill=(255,255,255,235))
    d.text((px+padx,py+pady-bb[1]),f,font=ffeat,fill=(11,74,92))
    px+=tw+2*padx+8*S

# rounded corners
mask=Image.new("L",(w,h),0)
ImageDraw.Draw(mask).rounded_rectangle([0,0,w-1,h-1],radius=26*S,fill=255)
img.putalpha(mask)
img.resize((W,H),Image.LANCZOS).save(OUT)
print("wrote",OUT)
