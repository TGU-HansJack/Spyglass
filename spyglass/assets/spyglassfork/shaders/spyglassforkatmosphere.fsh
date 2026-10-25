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
uniform int rainBeadCount;
uniform int rainTrailCount;
uniform vec4 rainDrops[32];
uniform vec4 rainBeads[32];
uniform vec4 rainTrails[192];
uniform float rainPool;
uniform vec2 rainInset;
uniform float flickProgress;
in vec2 texCoord;
layout(location = 0) out vec4 outColor;

// Beads and irregular runoff beads, in lens coordinates with Y pointing down.
float waterHeight(vec2 p)
{
    float height = 0.0;
    for (int i = 0; i < rainDropCount; i++)
    {
        vec4 drop = rainDrops[i];
        vec2 q = (p - drop.xy) / max(drop.z, 0.001);
        float moving = clamp(drop.w * 6.0, 0.0, 1.0);
        q.y /= 1.0 + moving * 0.65;
        q.x /= clamp(1.0 + q.y * moving * 0.20, 0.65, 1.2);
        float body = smoothstep(1.0, 0.38, dot(q, q));
        height = max(height, body);
    }
    for (int i = 0; i < rainBeadCount; i++)
    {
        vec4 bead = rainBeads[i];
        vec2 q = (p - bead.xy) / max(bead.z, 0.001);
        float body = max(0.0, 1.0 - dot(q, q));
        height = max(height, body * bead.w);
    }
    for (int i = 0; i < rainTrailCount; i++)
    {
        vec4 path = rainTrails[i * 2];
        vec4 shape = rainTrails[i * 2 + 1];
        float span = max(path.w - path.z, 0.001);
        float t = clamp((p.y - path.z) / span, 0.0, 1.0);
        float routeX = mix(path.x, path.y, t);
        float width = mix(shape.x, shape.y, t);
        float across = max(0.0, 1.0 - abs(p.x - routeX) / max(width, 0.00055));
        float along = smoothstep(path.z - .001, path.z + span * .22, p.y)
            * (1.0 - smoothstep(path.w - span * .22, path.w + .001, p.y));
        float fleck = max(0.0, across * max(along, 0.0)) * shape.z;
        height = max(height, fleck);
    }
    if (rainPool > 0.0001)
    {
        float top = 1.0 - rainPool * (0.85 + 0.10 * sin(p.x * 23.0) + 0.05 * sin(p.x * 59.0));
        height = max(height, smoothstep(top - 0.003, top + 0.005, p.y) * 0.65);
    }
    return clamp(height, 0.0, 1.0);
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
    float water = lensRainStrength > 0.0 ? waterHeight(lensPoint) : 0.0;
    if (any(lessThan(lensPoint, vec2(0.0))) || any(greaterThan(lensPoint, vec2(1.0)))) water = 0.0;
    vec2 normal = vec2(dFdx(water), dFdy(water)) * aperture.zw * frameSize / 128.0;
    vec2 refractedUv = clamp(texCoord + normal * lensRainStrength * 0.012,
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
    if (water > 0.0)
    {
        float edgeShade = clamp(length(normal) * 0.18, 0.0, 0.055);
        float highlight = clamp((-normal.x + normal.y) * 0.14, 0.0, 0.045) * clamp(lensLight, 0.0, 1.0);
        float shadeStep = floor(clamp(highlight - edgeShade - water * 0.09, -0.12, 0.045) * 16.0 + 0.5) / 16.0;
        color *= 1.0 + shadeStep * lensRainStrength;
    }
    if (flickProgress < 1.0)
    {
        // lensPoint.y is inverted above: 0,1 is top-left and 1,0 is
        // bottom-right, so both coordinates advance during the diagonal wipe.
        float sweep = (lensPoint.x + (1.0 - lensPoint.y)) * 0.5;
        float blade = exp(-pow((sweep - flickProgress) / 0.018, 2.0));
        color = mix(color, gradeAir(vec3(0.84, 0.88, 0.90)), blade * 0.20);
    }
    outColor = vec4(mix(scene.rgb, color, fade), scene.a);
}
