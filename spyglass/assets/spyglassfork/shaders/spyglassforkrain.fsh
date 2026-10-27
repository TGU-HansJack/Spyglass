#version 330 core
uniform int operation;
uniform vec2 surfaceSize;
uniform vec2 decay;
in vec2 worldPoint;
flat in vec4 dataA;
flat in vec4 dataB;
layout(location = 0) out vec2 outHeight;
float hash21(vec2 p)
{
    p = fract(p * vec2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}
float noiseAt(vec2 p)
{
    vec2 i = floor(p), f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash21(i), hash21(i + vec2(1, 0)), f.x),
        mix(hash21(i + vec2(0, 1)), hash21(i + vec2(1, 1)), f.x), f.y);
}
float wobble(vec2 p, float seed, float amount)
{
    float a = atan(p.y, p.x);
    return 1.0 + amount * (0.08 * sin(2.0*a + seed*31.0)
        + 0.05 * sin(3.0*a + seed*6.28) + 0.02 * sin(5.0*a + seed*17.0));
}
void main()
{
    if (operation == 0) { outHeight = decay; return; }
    float radius, distance, height;
    if (operation == 4)
    {
        radius = dataA.z;
        vec2 p = (worldPoint - dataA.xy) / max(radius, 0.001);
        p.y /= dataB.x;
        float stretch = dataB.z;
        if (p.y < 0.0)
        {
            float t = clamp(-p.y / (1.0 + stretch * 1.6), 0.0, 1.0);
            p.y /= 1.0 + stretch * 1.6;
            p.x *= 1.0 + stretch * 1.4 * t * t;
        }
        else p *= vec2(1.0 - 0.05 * stretch, 1.0 - 0.06 * stretch);
        distance = length(p) / wobble(p, dataB.y, clamp(1.0 - stretch * 1.5, 0.0, 1.0));
        float edge = (1.0 - distance) * radius;
        if (edge <= -0.5) discard;
        height = sqrt(max(0.0, 1.0 - distance * distance)) * radius * 0.5 * dataA.w * clamp(edge + 0.5, 0.0, 1.0);
        outHeight = vec2(height, radius); return;
    }
    radius = dataB.x;
    vec2 ab = dataA.zw - dataA.xy;
    float t = clamp(dot(worldPoint - dataA.xy, ab) / max(dot(ab, ab), 0.000001), 0.0, 1.0);
    distance = length(worldPoint - (dataA.xy + ab * t));
    if (operation == 1)
    {
        // A passing bead absorbs small droplets, while preserving the wet trail.
        outHeight = vec2(smoothstep(radius * 0.75, radius, distance), 1.0); return;
    }
    if (operation == 2)
    {
        vec2 p = (worldPoint - dataA.xy) / max(radius, 0.001);
        distance = length(p) / wobble(p, dataB.z, 0.5) * radius;
    }
    else
    {
        float scale = min(surfaceSize.x, surfaceSize.y) / 512.0;
        float n = noiseAt(worldPoint * (0.045 / scale) + dataB.z * 37.0);
        // The noise is fixed to the glass, so deposited trails do not crawl.
        radius *= mix(0.35, 1.0, n);
    }
    float edge = radius - distance;
    if (edge <= -0.5) discard;
    float q = distance / max(radius, 0.001);
    height = sqrt(max(0.0, 1.0 - q*q)) * dataB.x * dataB.y * clamp(edge + 0.5, 0.0, 1.0);
    outHeight = operation == 2 ? vec2(height, 0.0) : vec2(0.0, height);
}
