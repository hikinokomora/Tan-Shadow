#ifndef XUANWU_SNOW_BLEND_INCLUDED
#define XUANWU_SNOW_BLEND_INCLUDED
// Shader Graph Custom Function node, Name XuanwuSnow, File mode, precision Float.
// NormalWS: unperturbed geometric world normal. Noise, PaintedMask in [0,1].
void XuanwuSnow_float(float3 NormalWS,float Noise,float PaintedMask,float StartAngle,float EndAngle,out float SnowWeight)
{
    float y=saturate(normalize(NormalWS).y);
    float lo=cos(radians(EndAngle));
    float hi=cos(radians(StartAngle));
    SnowWeight=smoothstep(lo,max(lo+0.001,hi),y+(Noise-0.5)*0.12)*saturate(PaintedMask);
}
#endif
