using System;
using OpenTK.Graphics.OpenGL;

namespace spyglass.src.Client
{
    // No framebuffer, projection, viewport, blend function or scissor changes are
    // necessary. Restore the GL state touched by the pass, including texture units.
    internal sealed class OpticsRenderState : IDisposable
    {
        private readonly bool depth = GL.IsEnabled(EnableCap.DepthTest), blend = GL.IsEnabled(EnableCap.Blend), cull = GL.IsEnabled(EnableCap.CullFace);
        private readonly bool depthMask = GL.GetBoolean(GetPName.DepthWritemask);
        private readonly int program = GL.GetInteger(GetPName.CurrentProgram), vao = GL.GetInteger(GetPName.VertexArrayBinding);
        private readonly int arrayBuffer = GL.GetInteger(GetPName.ArrayBufferBinding);
        private readonly int blendSrcRgb = GL.GetInteger(GetPName.BlendSrcRgb), blendDstRgb = GL.GetInteger(GetPName.BlendDstRgb);
        private readonly int blendSrcAlpha = GL.GetInteger(GetPName.BlendSrcAlpha), blendDstAlpha = GL.GetInteger(GetPName.BlendDstAlpha);
        private readonly int activeTexture = GL.GetInteger(GetPName.ActiveTexture);
        private readonly int[] textures = new int[3], samplers = new int[3];

        internal OpticsRenderState()
        {
            for (int i = 0; i < textures.Length; i++)
            {
                GL.ActiveTexture(TextureUnit.Texture0 + i);
                textures[i] = GL.GetInteger(GetPName.TextureBinding2D);
                samplers[i] = GL.GetInteger(GetPName.SamplerBinding);
            }
            GL.ActiveTexture((TextureUnit)activeTexture);
        }

        public void Dispose()
        {
            Restore(EnableCap.DepthTest, depth);
            Restore(EnableCap.Blend, blend);
            Restore(EnableCap.CullFace, cull);
            GL.DepthMask(depthMask);
            GL.BlendFuncSeparate((BlendingFactorSrc)blendSrcRgb, (BlendingFactorDest)blendDstRgb,
                (BlendingFactorSrc)blendSrcAlpha, (BlendingFactorDest)blendDstAlpha);
            GL.UseProgram(program);
            GL.BindVertexArray(vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, arrayBuffer);
            for (int i = 0; i < textures.Length; i++)
            {
                GL.ActiveTexture(TextureUnit.Texture0 + i);
                GL.BindTexture(TextureTarget.Texture2D, textures[i]);
                GL.BindSampler(i, samplers[i]);
            }
            GL.ActiveTexture((TextureUnit)activeTexture);
        }

        private static void Restore(EnableCap flag, bool enabled)
        {
            if (enabled) GL.Enable(flag); else GL.Disable(flag);
        }
    }
}
