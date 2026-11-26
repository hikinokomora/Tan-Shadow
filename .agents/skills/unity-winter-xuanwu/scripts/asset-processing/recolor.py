"""Offline BaseColor-only processing; Pillow + numpy. Explicit inputs, no overwrite."""
import argparse
from pathlib import Path
import numpy as np
from PIL import Image

def transform(arr,profile):
 x=arr[...,:3].astype(np.float32)/255
 # linear-light luminance, then controlled tone mapping in sRGB
 linear=np.where(x<=.04045,x/12.92,((x+.055)/1.055)**2.4)
 l=linear@np.array([.2126,.7152,.0722],dtype=np.float32)
 lum=np.where(l<=.0031308,l*12.92,1.055*l**(1/2.4)-.055)[...,None]
 if profile=='K2': y=(.18*x+.82*lum)*.72*np.array([.83,.96,1.08])
 elif profile=='K3':
  green=np.clip((x[...,1]-np.maximum(x[...,0],x[...,2]))*8,0,1)[...,None];y=(x*(1-green)+lum*green)*np.array([.87,.94,1.01])
 elif profile=='K1':y=(.65*x+.35*lum)*np.array([.83,.91,1.0])
 elif profile=='K4':y=(.35*x+.65*lum)*np.array([.98,1.0,1.02])
 elif profile=='K5':y=(.30*x+.70*lum)*np.array([.94,.99,1.02])
 else:raise ValueError(profile)
 out=arr.copy();out[...,:3]=np.rint(np.clip(y,0,1)*255).astype(np.uint8);return out

def main():
 p=argparse.ArgumentParser();p.add_argument('--profile',choices=['K1','K2','K3','K4','K5'],required=True);p.add_argument('--out',type=Path,required=True);p.add_argument('files',nargs='+',type=Path);p.add_argument('--apply',action='store_true');a=p.parse_args(); targets=[]
 for f in a.files:
  n=f.stem.lower()
  if not any(t in n for t in ('basecolor','base_color','albedo','diffuse','_color','_col')) or any(t in n for t in ('normal','roughness','metallic','height','displacement','mask','ambientocclusion')):raise ValueError('Not explicit BaseColor: '+str(f))
  target=a.out/(f.stem+'_'+a.profile+'.png');targets.append((f,target));
 if len({t for _,t in targets})!=len(targets) or any(t.exists() for _,t in targets):raise FileExistsError('Output collision; no overwrites')
 for f,t in targets:print(f,'->',t)
 if not a.apply:return
 a.out.mkdir(parents=True,exist_ok=True)
 for f,t in targets:
  with Image.open(f) as im:
   if im.mode not in ('RGB','RGBA'):raise ValueError('Expected RGB/RGBA 8-bit sRGB; convert explicitly first')
   Image.fromarray(transform(np.array(im),a.profile)).save(t)
if __name__=='__main__':main()
