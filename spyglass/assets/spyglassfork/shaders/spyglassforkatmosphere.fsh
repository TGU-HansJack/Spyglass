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
uniform sampler2D rainBigTexture;
uniform sampler2D rainSmallTexture;
uniform vec2 rainTexel;
uniform vec2 rainInset;
in vec2 texCoord;
layout(location = 0) out vec4 outColor;

// APRG's persistent two-channel height field: tiny beads and deposited trails.
float heightAt(vec2 uv)
{
    if (any(lessThan(uv, vec2(0.0))) || any(greaterThan(uv, vec2(1.0)))) return 0.0;
    vec2 small = texture(rainSmallTexture, uv).rg;
    return max(texture(rainBigTexture, uv).r, max(small.r, small.g));
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
    vec2 waterUv = vec2(lensPoint.x, 1.0 - lensPoint.y);
    float height = lensRainStrength > 0.0 && onLens ? heightAt(waterUv) : 0.0;
    float mapScale = min(1.0 / rainTexel.x, 1.0 / rainTexel.y) / 512.0;
    float waterAlpha = clamp(height / max(0.6 * mapScale, 0.001), 0.0, 1.0);
    vec2 gradient = vec2(0.0);
    if (waterAlpha > 0.002)
        gradient = vec2(heightAt(waterUv + vec2(rainTexel.x, 0.0)) - heightAt(waterUv - vec2(rainTexel.x, 0.0)),
            heightAt(waterUv + vec2(0.0, rainTexel.y)) - heightAt(waterUv - vec2(0.0, rainTexel.y))) * 0.5;
    vec3 normal = normalize(vec3(-gradient, 1.0));
    vec2 big = texture(rainBigTexture, waterUv).rg;
    vec2 small = texture(rainSmallTexture, waterUv).rg;
    float lensRadius = big.r >= max(small.r, small.g) ? big.g : 3.0 * mapScale;
    vec2 shift = -normal.xy * (lensRadius * 1.4 + 2.0 * mapScale) * rainTexel
        * aperture.zw * (1.0 - 2.0 * rainInset) * lensRainStrength * waterAlpha;
    vec2 innerMin = aperture.xy + aperture.zw * rainInset + 0.5 / frameSize;
    vec2 innerMax = farEdge - aperture.zw * rainInset - 0.5 / frameSize;
    vec2 refractedUv = waterAlpha > 0.002 ? clamp(texCoord + shift, innerMin, innerMax) : texCoord;
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
    if (waterAlpha > 0.002)
    {
        // APRG's rounded lens, lower dark rim and upper reflection. Illumination
        // follows the game; unlit water cannot add light to a black scene.
        float illumination = clamp(lensLight, 0.0, 1.0);
        float rim = smoothstep(0.04, 0.55, 1.0 - normal.z);
        float bottom = clamp(-normal.y * 1.1 + 0.25, 0.0, 1.0);
        float dark = rim * mix(0.25, 1.0, bottom * bottom) * 0.65;
        float top = clamp(normal.y, 0.0, 1.0);
        vec3 halfLight = normalize(normalize(vec3(-0.35, 0.75, 0.55)) + vec3(0.0, 0.0, 1.0));
        float spec = pow(max(0.0, dot(normal, halfLight)), 90.0) * 0.28;
        vec3 wet = color * (1.0 + illumination * (0.04 + 0.10 * bottom)) * (1.0 - dark);
        wet += illumination * (rim * top * 0.10 + spec);
        color = mix(color, clamp(wet, 0.0, 1.0), clamp(waterAlpha * lensRainStrength, 0.0, 1.0));
    }
    outColor = vec4(mix(scene.rgb, color, fade), scene.a);
}
