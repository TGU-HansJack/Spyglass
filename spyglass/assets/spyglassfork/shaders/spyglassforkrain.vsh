#version 330 core
// Adapted from APRG, Copyright (c) 2026 DoraNeko, MIT; see NOTICE.txt.
uniform int operation;
uniform vec2 surfaceSize;
uniform vec4 instancesA[80];
uniform vec4 instancesB[80];
out vec2 worldPoint;
flat out vec4 dataA;
flat out vec4 dataB;
void main()
{
    vec2 corner = vec2(gl_VertexID & 1, gl_VertexID >> 1);
    vec2 point;
    dataA = vec4(0.0); dataB = vec4(0.0);
    if (operation == 0) point = corner * surfaceSize;
    else
    {
        vec4 a = instancesA[gl_InstanceID], b = instancesB[gl_InstanceID];
        float shorter = min(surfaceSize.x, surfaceSize.y);
        if (operation == 4)
        {
            dataA = vec4(a.xy * surfaceSize, a.z * shorter, a.w);
            dataB = b;
            vec2 lo = vec2(-1.35, -1.35 * b.x * (1.0 + b.z * 1.6));
            vec2 hi = vec2(1.35, 1.35 * b.x);
            point = dataA.xy + mix(lo, hi, corner) * dataA.z;
        }
        else
        {
            dataA = a * surfaceSize.xyxy;
            dataB = vec4(b.x * shorter, b.yzw);
            float padding = dataB.x * 1.3 + 1.5;
            point = mix(min(dataA.xy, dataA.zw) - padding, max(dataA.xy, dataA.zw) + padding, corner);
        }
    }
    worldPoint = point;
    gl_Position = vec4(point / surfaceSize * vec2(2.0, -2.0) + vec2(-1.0, 1.0), 0.0, 1.0);
}
