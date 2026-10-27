using System;
using OpenTK.Graphics.OpenGL;

namespace spyglass.src.Client
{
    // Covers both the offscreen water maps and the final optical composite.
    internal sealed class OpticsRenderState : IDisposable
    {
        private readonly bool depth = GL.IsEnabled(EnableCap.DepthTest), blend = GL.IsEnabled(EnableCap.Blend), cull = GL.IsEnabled(EnableCap.CullFace);
        private readonly bool depthMask = GL.GetBoolean(GetPName.DepthWritemask);
        private readonly bool scissor = GL.IsEnabled(EnableCap.ScissorTest);
        private readonly int drawFramebuffer = GL.GetInteger(GetPName.DrawFramebufferBinding), readFramebuffer = GL.GetInteger(GetPName.ReadFramebufferBinding);
        private readonly int equationRgb = GL.GetInteger(GetPName.BlendEquationRgb), equationAlpha = GL.GetInteger(GetPName.BlendEquationAlpha);
        private readonly int[] viewport = new int[4];
        private readonly float[] clearColor = new float[4];
        private readonly bool[] colorMask = new bool[4];
        private readonly int program = GL.GetInteger(GetPName.CurrentProgram), vao = GL.GetInteger(GetPName.VertexArrayBinding);
        private readonly int arrayBuffer = GL.GetInteger(GetPName.ArrayBufferBinding);
        private readonly int blendSrcRgb = GL.GetInteger(GetPName.BlendSrcRgb), blendDstRgb = GL.GetInteger(GetPName.BlendDstRgb);
        private readonly int blendSrcAlpha = GL.GetInteger(GetPName.BlendSrcAlpha), blendDstAlpha = GL.GetInteger(GetPName.BlendDstAlpha);
        private readonly int activeTexture = GL.GetInteger(GetPName.ActiveTexture);
        private readonly int[] textures = new int[4], samplers = new int[4];

        internal OpticsRenderState()
        {
            GL.GetInteger(GetPName.Viewport, viewport);
            GL.GetFloat(GetPName.ColorClearValue, clearColor);
            GL.GetBoolean(GetPName.ColorWritemask, colorMask);
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
            Restore(EnableCap.ScissorTest, scissor);
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, drawFramebuffer);
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, readFramebuffer);
            GL.Viewport(viewport[0], viewport[1], viewport[2], viewport[3]);
            GL.ClearColor(clearColor[0], clearColor[1], clearColor[2], clearColor[3]);
            GL.ColorMask(colorMask[0], colorMask[1], colorMask[2], colorMask[3]);
            GL.BlendEquationSeparate((BlendEquationMode)equationRgb, (BlendEquationMode)equationAlpha);
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
