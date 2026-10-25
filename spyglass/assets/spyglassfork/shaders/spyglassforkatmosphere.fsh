#version 330 core
uniform sampler2D sceneTexture;
uniform sampler2D depthTexture;
uniform sampler2D frostTexture;
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
uniform vec2 rainInset;
uniform float flickProgress;
in vec2 texCoord;
layout(location = 0) out vec4 outColor;

// Analytic dome normals stay visible against a uniform sky, where sampling
// scene detail alone cannot reveal a lens. zw hold coverage and the contact rim.
vec4 beadSurface(vec2 p)
{
    vec4 surface = vec4(0.0);
    for (int i = 0; i < rainDropCount; i++)
    {
        vec4 bead = rainDrops[i];
        vec2 q = (p - bead.xy) / max(bead.z, 0.001);
        q.y /= rainDropShapes[i].x;
        float r2 = dot(q, q);
        if (r2 >= 1.0) continue;
        float coverage = (1.0 - smoothstep(0.88, 1.0, r2)) * bead.w;
        float rim = smoothstep(0.36, 0.76, r2) * coverage;
        if (coverage > surface.z)
        {
            vec2 normal = q / max(0.38, sqrt(max(0.0, 1.0 - r2)));
            normal.y = -normal.y; // lens-local Y points down, texture Y points up
            surface = vec4(normal * bead.w, coverage, rim);
        }
    }
    return surface;
}

// The reference's blue background is absent from this alpha texture. Only
// feathered ice detail is sampled; the world remains visible between crystals.
vec2 frostTextureCoverage(vec2 p, vec4 crystal, vec4 detail)
{
    vec2 delta = p - crystal.xy;
    vec2 axis = vec2(cos(detail.x), sin(detail.x));
    vec2 q = vec2(dot(delta, axis), dot(delta, vec2(-axis.y, axis.x))) / crystal.z;
    vec2 uv = vec2(q.x, q.y + 0.5);
    if (any(lessThan(uv, vec2(0.0))) || any(greaterThan(uv, vec2(1.0)))) return vec2(0.0);
    if (detail.z > 0.5) uv.y = 1.0 - uv.y;
    float ice = texture(frostTexture, uv).a;
    float reveal = 1.0 - smoothstep(detail.y - 0.025, detail.y + 0.025, uv.x);
    reveal *= smoothstep(-0.005, 0.015, detail.y);
    float ridge = 1.0 - exp(-ice * 3.0);
    return vec2(ridge, ice) * reveal * crystal.w;
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
    vec2 frost = vec2(0.0);
    for (int i = 0; i < frostCount; i++)
        frost = max(frost, frostTextureCoverage(lensPoint, frostCrystals[i], frostDetails[i]));
    if (!onLens) frost = vec2(0.0);
    // Thin ice and droplets both transmit a displaced scene. Physical pixel
    // limits prevent excessive distortion on large monitors and zoom settings.
    vec2 iceNormal = vec2(dFdx(frost.y), dFdy(frost.y));
    vec2 iceShift = clamp(iceNormal * 16.0, vec2(-2.5), vec2(2.5)) * step(0.001, frost.y);
    vec2 beadShift = clamp(bead.xy * 2.0 * lensRainStrength, vec2(-4.0), vec2(4.0));
    vec2 refractedUv = clamp(texCoord + (beadShift + iceShift) / frameSize,
        aperture.xy + 0.5 / frameSize, farEdge - 0.5 / frameSize);
    vec3 color = texture(sceneTexture, refractedUv).rgb;
    vec2 roughness = vec2(1.1) / frameSize;
    vec3 roughScene = (texture(sceneTexture, clamp(refractedUv + roughness, aperture.xy, farEdge)).rgb
        + texture(sceneTexture, clamp(refractedUv - roughness, aperture.xy, farEdge)).rgb) * 0.5;
    color = mix(color, roughScene, frost.y * 0.22);
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
    float skyBrightness = smoothstep(0.35, 0.95, luminance);
    vec2 sunDelta = (sunScreen - texCoord) * frameSize;
    vec2 incidentLight = glareStrength > 0.001 && length(sunDelta) > 1.0
        ? normalize(sunDelta) : normalize(vec2(-0.6, 0.8));
    if (bead.z > 0.0)
    {
        vec2 n = bead.xy;
        // At a grazing angle the water contact reflects darker surroundings.
        // Paired dark/light contours survive bright fog where white highlights
        // and refraction of a constant color would otherwise disappear.
        float grazing = clamp(length(n) / 2.2, 0.0, 1.0);
        float darkRim = bead.w * (0.22 + skyBrightness * 0.30) * illumination;
        float refractiveBody = bead.z * (0.016 + grazing * 0.012);
        color *= 1.0 - clamp((darkRim + refractiveBody) * lensRainStrength, 0.0, 0.62);
        float facing = max(0.0, dot(normalize(n + vec2(0.00001)), incidentLight));
        float glint = pow(facing, 9.0) * bead.w * 0.50 * illumination;
        color = mix(color, vec3(0.96, 0.98, 1.0), clamp(glint * lensRainStrength, 0.0, 0.65));
    }
    // Ice ridges need both reflected light and a darker refractive boundary:
    // a white-only reflection cannot stand out against sunlit white clouds.
    float iceEdge = clamp(length(iceNormal) * 3.5, 0.0, 1.0);
    float iceContour = frost.x * (0.10 + skyBrightness * 0.23) + iceEdge * 0.15;
    color *= 1.0 - clamp(iceContour * illumination, 0.0, 0.46);
    float iceReflection = (frost.x * 0.20 + clamp(dot(iceNormal, incidentLight), 0.0, 0.35) * 0.55)
        * illumination;
    color = mix(color, gradeAir(vec3(0.92, 0.98, 1.0)), clamp(iceReflection, 0.0, 0.60));
    if (flickProgress < 1.0)
    {
        // lensPoint uses top-left origin after the screen UV Y flip.
        float sweep = (lensPoint.x + lensPoint.y) * 0.5;
        float blade = exp(-pow((sweep - flickProgress) / 0.018, 2.0));
        color = mix(color, gradeAir(vec3(0.84, 0.88, 0.90)), blade * 0.20);
    }
    outColor = vec4(mix(scene.rgb, color, fade), scene.a);
}
