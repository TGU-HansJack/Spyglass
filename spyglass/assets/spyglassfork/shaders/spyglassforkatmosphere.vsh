#version 330 core
layout(location = 0) in vec3 vertexPositionIn;
out vec2 texCoord;

void main()
{
    gl_Position = vec4(vertexPositionIn.xy, 0.0, 1.0);
    texCoord = vertexPositionIn.xy * 0.5 + 0.5;
}
