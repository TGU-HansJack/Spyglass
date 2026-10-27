using System;
using OpenTK.Graphics.OpenGL;

namespace spyglass.src.Client
{
    // Persistent RG16F small-drop/trail height map, plus a transient bead map.
    // No desktop capture or Direct3D dependencies; the game supplies the background.
    internal sealed class LensRainSurface : IDisposable
    {
        private int program, vao, smallFbo, bigFbo, smallTexture, bigTexture;
        private int generation = -1;
        private LensRainModel owner = null!;
        private readonly int operation, surfaceSize, decay, instancesA, instancesB;
        private readonly float[] a = new float[LensRainModel.MaxDrops * 4], b = new float[LensRainModel.MaxDrops * 4];
        internal int SmallTexture => smallTexture;
        internal int BigTexture => bigTexture;
        internal int Width { get; private set; }
        internal int Height { get; private set; }

        internal LensRainSurface(string vertexSource, string fragmentSource)
        {
            int vertex = 0, fragment = 0;
            try
            {
                vertex = Compile(ShaderType.VertexShader, vertexSource);
                fragment = Compile(ShaderType.FragmentShader, fragmentSource);
                program = GL.CreateProgram();
                GL.AttachShader(program, vertex); GL.AttachShader(program, fragment); GL.LinkProgram(program);
                GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
                if (linked == 0) throw new InvalidOperationException("Rain surface link: " + GL.GetProgramInfoLog(program));
                vao = GL.GenVertexArray();
                operation = GL.GetUniformLocation(program, "operation");
                surfaceSize = GL.GetUniformLocation(program, "surfaceSize");
                decay = GL.GetUniformLocation(program, "decay");
                instancesA = GL.GetUniformLocation(program, "instancesA");
                instancesB = GL.GetUniformLocation(program, "instancesB");
            }
            catch { Dispose(); throw; }
            finally { if (vertex != 0) GL.DeleteShader(vertex); if (fragment != 0) GL.DeleteShader(fragment); }
        }

        private static int Compile(ShaderType type, string source)
        {
            int id = GL.CreateShader(type);
            GL.ShaderSource(id, source); GL.CompileShader(id);
            GL.GetShader(id, ShaderParameter.CompileStatus, out int compiled);
            if (compiled != 0) return id;
            string log = GL.GetShaderInfoLog(id); GL.DeleteShader(id);
            throw new InvalidOperationException("Rain surface " + type + ": " + log);
        }

        private static void MakeTarget(int width, int height, ref int texture, ref int fbo)
        {
            texture = GL.GenTexture(); GL.BindTexture(TextureTarget.Texture2D, texture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rg16f, width, height, 0, PixelFormat.Rg, PixelType.Float, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            fbo = GL.GenFramebuffer(); GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, fbo);
            GL.FramebufferTexture2D(FramebufferTarget.DrawFramebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, texture, 0);
            if (GL.CheckFramebufferStatus(FramebufferTarget.DrawFramebuffer) != FramebufferErrorCode.FramebufferComplete)
                throw new InvalidOperationException("Incomplete lens rain framebuffer");
            GL.Clear(ClearBufferMask.ColorBufferBit);
        }

        // Call inside OpticsRenderState. UV positions and deposited water survive resizing.
        internal void Update(LensRainModel model, int width, int height)
        {
            if (!ReferenceEquals(owner, model)) { owner = model; generation = -1; }
            GL.Disable(EnableCap.ScissorTest); GL.Disable(EnableCap.DepthTest); GL.DepthMask(false);
            GL.Disable(EnableCap.CullFace); GL.ColorMask(true, true, true, true);
            GL.ClearColor(0, 0, 0, 0);
            // Limit texture memory/work on very large monitors; retain aspect ratio.
            float scale = Math.Min(1, 1024f / Math.Max(width, height));
            width = Math.Max(64, (int)(width * scale)); height = Math.Max(64, (int)(height * scale));
            GL.ActiveTexture(TextureUnit.Texture2);
            float heightScale = 1;
            if (width != Width || height != Height)
            {
                if (smallTexture == 0)
                {
                    MakeTarget(width, height, ref smallTexture, ref smallFbo);
                    MakeTarget(width, height, ref bigTexture, ref bigFbo);
                }
                else
                {
                    // Keep texture/FBO identities: saved GL state may reference them.
                    // A temporary target preserves the old height field during resize.
                    int copy = 0, copyFbo = 0;
                    bool preserve = generation == model.Generation;
                    try
                    {
                        if (preserve)
                        {
                            MakeTarget(width, height, ref copy, ref copyFbo);
                            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, smallFbo);
                            GL.BlitFramebuffer(0, 0, Width, Height, 0, 0, width, height, ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Linear);
                            heightScale = (float)Math.Min(width, height) / Math.Min(Width, Height);
                        }
                        foreach (int texture in new[] { smallTexture, bigTexture })
                        {
                            GL.BindTexture(TextureTarget.Texture2D, texture);
                            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rg16f, width, height, 0, PixelFormat.Rg, PixelType.Float, IntPtr.Zero);
                        }
                        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, bigFbo); GL.Clear(ClearBufferMask.ColorBufferBit);
                        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, smallFbo); GL.Clear(ClearBufferMask.ColorBufferBit);
                        if (preserve)
                        {
                            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, copyFbo);
                            GL.BlitFramebuffer(0, 0, width, height, 0, 0, width, height, ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
                        }
                    }
                    finally
                    {
                        if (copyFbo != 0) GL.DeleteFramebuffer(copyFbo);
                        if (copy != 0) GL.DeleteTexture(copy);
                    }
                }
                Width = width; Height = height;
                generation = model.Generation;
            }
            GL.Viewport(0, 0, width, height);
            GL.UseProgram(program); GL.BindVertexArray(vao);
            GL.Uniform2(surfaceSize, (float)width, (float)height);
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, smallFbo);
            if (generation != model.Generation)
            {
                GL.Clear(ClearBufferMask.ColorBufferBit); generation = model.Generation;
            }
            GL.Enable(EnableCap.Blend);
            GL.BlendEquation(BlendEquationMode.FuncAdd);
            GL.BlendFunc(BlendingFactor.Zero, BlendingFactor.SrcColor);
            GL.Uniform1(operation, 0);
            GL.Uniform2(decay, heightScale * MathF.Pow(.88f, model.PendingSeconds), heightScale * MathF.Pow(.55f, model.PendingSeconds));
            GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
            // Preserve command order: an absorbing drop may erase an older stamp.
            for (int start = 0; start < model.Stamps.Count;)
            {
                var kind = model.Stamps[start].Kind;
                int count = 0;
                while (start < model.Stamps.Count && count < LensRainModel.MaxDrops && model.Stamps[start].Kind == kind)
                {
                    var s = model.Stamps[start++]; int j = count++ * 4;
                    a[j] = s.X0; a[j+1] = s.Y0; a[j+2] = s.X1; a[j+3] = s.Y1;
                    float age = (float)Math.Max(0, model.SurfaceTime - s.Time);
                    b[j] = s.Radius; b[j+1] = s.Height * MathF.Pow(kind == LensRainModel.StampKind.Small ? .88f : .55f, age);
                    b[j+2] = s.Seed; b[j+3] = 0;
                }
                bool erase = kind == LensRainModel.StampKind.Erase;
                GL.BlendEquation(erase ? BlendEquationMode.FuncAdd : BlendEquationMode.Max);
                GL.BlendFunc(erase ? BlendingFactor.Zero : BlendingFactor.One, erase ? BlendingFactor.SrcColor : BlendingFactor.One);
                Draw(erase ? 1 : kind == LensRainModel.StampKind.Small ? 2 : 3, count, a, b);
            }
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, bigFbo);
            GL.Clear(ClearBufferMask.ColorBufferBit);
            GL.BlendEquation(BlendEquationMode.Max); GL.BlendFunc(BlendingFactor.One, BlendingFactor.One);
            Draw(4, model.Drops.Count, model.DropUniforms, model.DropShapeUniforms);
            model.AcknowledgeSurface();
        }

        private void Draw(int mode, int count, float[] dataA, float[] dataB)
        {
            if (count == 0) return;
            GL.Uniform1(operation, mode);
            GL.Uniform4(instancesA, count, dataA); GL.Uniform4(instancesB, count, dataB);
            GL.DrawArraysInstanced(PrimitiveType.TriangleStrip, 0, 4, count);
        }

        private void ReleaseTargets()
        {
            if (smallFbo != 0) GL.DeleteFramebuffer(smallFbo);
            if (bigFbo != 0) GL.DeleteFramebuffer(bigFbo);
            if (smallTexture != 0) GL.DeleteTexture(smallTexture);
            if (bigTexture != 0) GL.DeleteTexture(bigTexture);
            smallFbo = bigFbo = smallTexture = bigTexture = 0;
        }
        public void Dispose()
        {
            ReleaseTargets();
            if (vao != 0) GL.DeleteVertexArray(vao);
            if (program != 0) GL.DeleteProgram(program);
            vao = program = 0;
        }
    }
}
