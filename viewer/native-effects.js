// Decode original EGA/VGA pixels only. Effect simulation and rasterization stay in DOS.
export function decodeEffectPlanes(planes,palette) {
  if(planes.length!==32000||palette.length!==16)throw new Error('Invalid native effects frame');
  const rgba=new Uint8ClampedArray(320*200*4);
  for(let byte=0;byte<8000;byte++)for(let bit=0;bit<8;bit++){
    const shift=7-bit;
    const index=((planes[byte]>>shift)&1)|(((planes[byte+8000]>>shift)&1)<<1)
      |(((planes[byte+16000]>>shift)&1)<<2)|(((planes[byte+24000]>>shift)&1)<<3);
    const p=(byte*8+bit)*4,colour=palette[index];
    rgba[p]=colour[0];rgba[p+1]=colour[1];rgba[p+2]=colour[2];rgba[p+3]=index?255:0;
  }
  return rgba;
}
