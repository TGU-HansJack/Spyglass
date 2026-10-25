#version 330 core
uniform sampler2D sceneTexture;
uniform sampler2D depthTexture;
uniform mat4 inverseProjection;
uniform vec4 aperture;
uniform vec2 frameSize;
uniform vec3 fogColor;
uniform vec4 colorGrade;
uniform float sepia;
uniform float fade;
uniform float extinction;
uniform float dayWeight;
uniform float glareStrength;
uniform vec3 sunViewDirection;
uniform vec2 sunScreen;
uniform float lensRainStrength;
uniform float lensLight;
uniform int rainDropCount;
uniform vec4 rainDrops[80];
uniform vec4 rainDropShapes[80];
uniform int frostCount;
uniform vec4 frostCrystals[2];
uniform vec4 frostDetails[2];
uniform vec4 frostFacets[144];
uniform vec4 frostFacetShapes[144];
uniform vec2 rainInset;
in vec2 texCoord;
layout(location = 0) out vec4 outColor;

// Restore the original rounded height profile: optical transmission is
// driven by thickness gradients, not a drawn contact ring. zw: height, thin rim.
vec4 beadSurface(vec2 p)
{
    vec4 surface = vec4(0.0);
    for (int i = 0; i < rainDropCount; i++)
    {
        vec4 bead = rainDrops[i];
        vec2 q = (p - bead.xy) / max(bead.z, 0.001);
        q.y /= rainDropShapes[i].x;
        float r2 = dot(q, q);
        float body = max(0.0, 1.0 - r2);
        float thickness = body * body * bead.w;
        surface.z = max(surface.z, thickness);
        float contact = smoothstep(0.72, 0.88, r2) * (1.0 - smoothstep(0.94, 1.0, r2));
        surface.w = max(surface.w, contact * bead.w);
    }
    return surface;
}

// Shared water/ice lens response, matching the pre-v0.7.8 bead optics.
vec2 transmissionNormal(float thickness, vec2 physicalSize)
{
    return vec2(dFdx(thickness), dFdy(thickness)) * physicalSize / 128.0;
}

// Each tapered crystal is actual analytic prism geometry with a triangular
// cross-section. No image sampling, derivative-of-texture normal or white decal.
// xy: screen-oriented facet normal, z: transparent coverage, w: ridge highlight.
vec4 proceduralFrost(vec2 p, vec4 crystal, vec4 detail, int patchIndex, vec2 pixelSize)
{
    vec2 axis = vec2(cos(detail.x), sin(detail.x));
    vec2 perpendicular = vec2(-axis.y, axis.x);
    vec2 delta = p - crystal.xy;
    vec2 q = vec2(dot(delta, axis), dot(delta, perpendicular)) / crystal.z;
    if (q.x < -0.04 || q.x > 1.08 || abs(q.y) > 0.62 || detail.y <= 0.0) return vec4(0.0);
    float aa = max(pixelSize.x, pixelSize.y) / crystal.z * 0.55;
    vec4 result = vec4(0.0);
    float strongest = 0.0;
    for (int facetIndex = 0; facetIndex < 72; facetIndex++)
    {
        if (facetIndex >= int(detail.w)) break;
        int index = patchIndex * 72 + facetIndex;
        vec4 facet = frostFacets[index];
        vec2 shape = frostFacetShapes[index].xy;
        vec2 a = facet.xy, b = facet.zw;
        float grown = clamp((detail.y - a.x) / max(b.x - a.x, 0.01), 0.0, 1.0);
        if (grown <= 0.0) continue;
        b = mix(a, b, grown);
        vec2 direction = b - a;
        float facetLength = length(direction);
        if (facetLength < 0.0001) continue;
        direction /= facetLength;
        vec2 side = vec2(-direction.y, direction.x);
        float distanceAlong = dot(q - a, direction);
        float t = clamp(distanceAlong / facetLength, 0.0, 1.0);
        float width = shape.x * (1.0 - t * 0.90);
        float lateral = dot(q - a, side);
        float extent = smoothstep(-aa, aa, distanceAlong)
            * (1.0 - smoothstep(facetLength - aa, facetLength + aa, distanceAlong));
        float coverage = (1.0 - smoothstep(max(0.0, width - aa), width + aa, abs(lateral))) * extent;
        // Integrate subpixel twigs: fine ice must fade to partial coverage,
        // not vanish or alias as the display size changes.
        coverage *= clamp(width / max(aa, 0.0001), 0.0, 1.0) * crystal.w;
        float ridge = exp(-abs(lateral) / max(width * 0.28, aa)) * coverage;
        float across = clamp(1.0 - abs(lateral) / max(width + aa, 0.0001), 0.0, 1.0);
        float roundedThickness = across * across * coverage * shape.y;
        float score = roundedThickness;
        if (score > strongest)
        {
            float slope = lateral / sqrt(lateral * lateral + aa * aa);
            vec2 localNormal = side * slope * shape.y * 0.85 - direction * 0.10;
            vec2 lensNormal = axis * localNormal.x + perpendicular * localNormal.y;
            lensNormal.y = -lensNormal.y;
            result.xy = lensNormal * coverage;
            strongest = score;
        }
        result.z = max(result.z, score);
        result.w = max(result.w, ridge * shape.y);
    }
    return result;
}

float hueChannel(float lo, float hi, float h)
{
    h = fract(h);
    if (h < 1.0 / 6.0) return lo + (hi - lo) * 6.0 * h;
    if (h < 0.5) return hi;
    if (h < 2.0 / 3.0) return lo + (hi - lo) * (2.0 / 3.0 - h) * 6.0;
    return lo;
}

// Match the engine final pass's gamma, brightness, sepia and contrast for
// atmospheric colors. The scene has already received the engine color grade.
vec3 gradeAir(vec3 color)
{
    color = clamp(color, 0.0, 1.0);
    vec3 source = color;
    float gradeWeight = dot(source, vec3(0.299, 0.587, 0.114));
    float lo = min(color.r, min(color.g, color.b));
    float hi = max(color.r, max(color.g, color.b));
    float delta = hi - lo;
    float light = (hi + lo) * 0.5;
    float sat = 0.0;
    float hue = 0.0;
    if (delta > 0.000001)
    {
        sat = light < 0.5 ? delta / (hi + lo) : delta / (2.0 - hi - lo);
        if (hi == color.r) hue = (color.g - color.b) / delta;
        else if (hi == color.g) hue = 2.0 + (color.b - color.r) / delta;
        else hue = 4.0 + (color.r - color.g) / delta;
        hue = fract(hue / 6.0);
    }
    light = pow(light, 1.0 / max(colorGrade.x, 0.001));
    float upper = light < 0.5 ? light * (1.0 + sat) : light + sat - light * sat;
    float lower = 2.0 * light - upper;
    color = vec3(hueChannel(lower, upper, hue + 1.0/3.0), hueChannel(lower, upper, hue), hueChannel(lower, upper, hue - 1.0/3.0));
    color = pow(max(color, 0.0), vec3(1.0 / max(colorGrade.y, 0.001))) * colorGrade.z;
    vec3 warm = vec3(dot(color, vec3(0.393, 0.769, 0.189)), dot(color, vec3(0.349, 0.686, 0.168)), dot(color, vec3(0.272, 0.534, 0.131))) * 0.85;
    color = mix(color, warm, sepia) * (colorGrade.w + 1.0) - colorGrade.w;
    color /= max(1.0, (color.r + color.g + color.b) / 3.0);
    // The engine mixes its grade by the original luminance in the final pass.
    return clamp(mix(source, color, gradeWeight), 0.0, 1.0);
}

void main()
{
    vec2 farEdge = aperture.xy + aperture.zw;
    if (any(lessThan(texCoord, aperture.xy)) || any(greaterThan(texCoord, farEdge))) discard;
    vec4 scene = texture(sceneTexture, texCoord);
    vec2 lensPoint = ((texCoord - aperture.xy) / aperture.zw - rainInset) / max(vec2(0.01), 1.0 - 2.0 * rainInset);
    lensPoint.y = 1.0 - lensPoint.y;
    bool onLens = all(greaterThanEqual(lensPoint, vec2(0.0))) && all(lessThanEqual(lensPoint, vec2(1.0)));
    vec4 bead = lensRainStrength > 0.0 && onLens ? beadSurface(lensPoint) : vec4(0.0);
    vec4 frost = vec4(0.0);
    vec2 lensPixels = max(aperture.zw * frameSize * (1.0 - 2.0 * rainInset), vec2(1.0));
    for (int i = 0; i < frostCount; i++)
    {
        vec4 patch = proceduralFrost(lensPoint, frostCrystals[i], frostDetails[i], i, 1.0 / lensPixels);
        if (patch.z > frost.z) frost.xy = patch.xy;
        frost.zw = max(frost.zw, patch.zw);
    }
    if (!onLens) frost = vec4(0.0);
    vec2 physicalSize = aperture.zw * frameSize;
    vec2 beadNormal = transmissionNormal(bead.z, physicalSize);
    vec2 iceNormal = transmissionNormal(frost.z, physicalSize);
    vec2 beadShift = beadNormal * lensRainStrength * 0.012;
    vec2 iceShift = iceNormal * 0.012;
    // Only bound exceptionally steep subpixel ice slopes. Water retains its
    // original lens-like displacement, including radius-dependent magnification.
    iceShift = clamp(iceShift, -vec2(8.0) / frameSize, vec2(8.0) / frameSize);
    vec2 refractedUv = clamp(texCoord + beadShift + iceShift,
        aperture.xy + 0.5 / frameSize, farEdge - 0.5 / frameSize);
    vec3 color = texture(sceneTexture, refractedUv).rgb;
    float depth = texture(depthTexture, texCoord).r;
    vec4 view = inverseProjection * vec4(texCoord * 2.0 - 1.0, depth * 2.0 - 1.0, 1.0);
    // A cleared depth represents the sky, which keeps the engine's existing fog.
    if (depth < 1.0 && abs(view.w) > 0.000001)
    {
        float distance = length(view.xyz / view.w);
        float amount = min(0.30, 1.0 - exp(-extinction * distance));
        vec3 weather = mix(color, gradeAir(fogColor), amount);
        // At night extra scattering must never reveal dark targets by brightening them.
        color = mix(min(weather, color), weather, dayWeight);
    }

    vec2 delta = (texCoord - sunScreen) * frameSize;
    float apertureSize = min(aperture.z * frameSize.x, aperture.w * frameSize.y);
    float radius = max(1.0, apertureSize * 0.35);
    vec2 outside = max(max(aperture.xy - sunScreen, sunScreen - farEdge), vec2(0.0)) * frameSize;
    float inView = 1.0 - smoothstep(0.0, apertureSize * 0.15, length(outside));
    vec4 ray = inverseProjection * vec4(texCoord * 2.0 - 1.0, 0.0, 1.0);
    float angular = smoothstep(0.9781476, 1.0, dot(normalize(ray.xyz), normalize(sunViewDirection)));
    float halo = exp(-4.0 * dot(delta, delta) / (radius * radius)) * angular * inView * glareStrength;
    color = mix(color, gradeAir(vec3(1.0, 0.97, 0.90)), clamp(halo, 0.0, 0.20));
    float illumination = clamp(lensLight, 0.0, 1.0);
    float luminance = dot(color, vec3(0.299, 0.587, 0.114));
    vec2 sunDelta = (sunScreen - texCoord) * frameSize;
    vec2 incidentLight = glareStrength > 0.001 && length(sunDelta) > 1.0
        ? normalize(sunDelta) : normalize(vec2(-0.6, 0.8));
    if (bead.z > 0.0)
    {
        // The original water transmission shading is kept verbatim.
        float edgeShade = clamp(length(beadNormal) * 0.18, 0.0, 0.055);
        float highlight = clamp((-beadNormal.x + beadNormal.y) * 0.14, 0.0, 0.045) * illumination;
        color *= 1.0 + (highlight - edgeShade - bead.z * 0.055) * lensRainStrength;
        // A thin supplemental rim only on bright sky; do not replace the dome
        // with the broad, dark circles introduced by the visibility correction.
        float brightRim = bead.w * smoothstep(0.60, 0.95, luminance) * illumination;
        color *= 1.0 - brightRim * 0.28 * lensRainStrength;
    }
    if (frost.z > 0.0)
    {
        // Ice uses the same curved thickness -> normal -> transmitted scene
        // chain as water. Its geometry is still an asymmetric feathered crystal.
        float edgeShade = clamp(length(iceNormal) * 0.18, 0.0, 0.055);
        float highlight = clamp((-iceNormal.x + iceNormal.y) * 0.14, 0.0, 0.045) * illumination;
        color *= 1.0 + highlight - edgeShade - frost.z * 0.055;
        float brightEdge = smoothstep(0.60, 0.95, luminance) * illumination;
        float crystalBoundary = clamp(length(iceNormal) * 0.6, 0.0, 0.28) * frost.z;
        color *= 1.0 - crystalBoundary * brightEdge;
        float facetReflection = pow(max(0.0, dot(frost.xy, incidentLight)), 2.0) * 0.10 * illumination;
        color = mix(color, gradeAir(vec3(0.92, 0.98, 1.0)), facetReflection);
    }
    outColor = vec4(mix(scene.rgb, color, fade), scene.a);
}
