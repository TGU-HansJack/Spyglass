using OpenTK.Graphics.OpenGL;
using spyglass.src.Client;

// Runs the production shader on a supplied test image, never a reimplementation.
internal static class OpticsPreview
{
    public static void Render(int program, int vao, string directory)
    {
        const int size = 1044, side = 512, origin = (size - side) / 2;
        var input = File.ReadAllBytes(Path.Combine(directory, "scene.rgba"));
        if (input.Length != size * size * 4) throw new InvalidDataException("Expected 1044x1044 RGBA input.");
        int Texture()
        {
            int id = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, id);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            return id;
        }
        GL.ActiveTexture(TextureUnit.Texture0); GL.BindSampler(0, 0);
        int scene = Texture();
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, size, size, 0, PixelFormat.Rgba, PixelType.UnsignedByte, input);
        // A flat reference plane at 256 blocks; not inferred game depth.
        const double near = .1, far = 4096, fov = .18;
        double f = 1 / Math.Tan(fov / 2);
        double[] projection = [f,0,0,0, 0,f,0,0, 0,0,-(far+near)/(far-near),-1, 0,0,-2*far*near/(far-near),0];
        float d = (float)((projection[10] * -256 + projection[14]) / 256 * .5 + .5);
        GL.ActiveTexture(TextureUnit.Texture1); GL.BindSampler(1, 0);
        int depth = Texture();
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R32f, 1, 1, 0, PixelFormat.Red, PixelType.Float, new[] { d });
        GL.ActiveTexture(TextureUnit.Texture3);
        int output = Texture();
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, size, size, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
        int framebuffer = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, output, 0);
        if (GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != FramebufferErrorCode.FramebufferComplete)
            throw new InvalidOperationException("Preview framebuffer incomplete.");
        GL.UseProgram(program); GL.BindVertexArray(vao);
        GL.Viewport(0, 0, size, size);
        GL.Disable(EnableCap.DepthTest); GL.Disable(EnableCap.Blend); GL.Disable(EnableCap.CullFace);
        int U(string name) => GL.GetUniformLocation(program, name);
        void F(string name, float value) => GL.Uniform1(U(name), value);
        GL.Uniform1(U("sceneTexture"), 0); GL.Uniform1(U("depthTexture"), 1);
        GL.Uniform2(U("frameSize"), (float)size, (float)size);
        GL.Uniform4(U("aperture"), origin / (float)size, origin / (float)size, side / (float)size, side / (float)size);
        GL.Uniform3(U("fogColor"), .72f, .77f, .80f);
        GL.Uniform4(U("colorGrade"), 1f, 1f, 1f, 0f);
        F("sepia", 0); F("fade", 1); F("dayWeight", 1);
        var inverse = new float[16];
        if (!AtmosphericOpticsModel.InvertProjection(projection, inverse)) throw new InvalidOperationException("Preview projection singular.");
        GL.UniformMatrix4(U("inverseProjection"), 1, false, inverse);
        float sunX = (origin + side * .67f) / size, sunY = (origin + side * .67f) / size;
        GL.Uniform2(U("sunScreen"), sunX, sunY);
        GL.Uniform3(U("sunViewDirection"), (float)((sunX * 2 - 1) / f), (float)((sunY * 2 - 1) / f), -1f);
        var cases = new[] {
            ("off", 0f, 0f),
            ("rain", AtmosphericOpticsModel.Extinction(OpticsPrecipitation.Rain, 1, 1), 0f),
            ("snow", AtmosphericOpticsModel.Extinction(OpticsPrecipitation.Snow, 1, 1), 0f),
            ("glare", 0f, AtmosphericOpticsModel.Glare(1, 1, 0, 0, 1)),
            ("rain2", AtmosphericOpticsModel.Extinction(OpticsPrecipitation.Rain, 1, 2), 0f),
            ("glare2", 0f, AtmosphericOpticsModel.Glare(1, 1, 0, 0, 2))
        };
        foreach (var (name, extinction, glare) in cases)
        {
            F("extinction", extinction); F("glareStrength", glare);
            GL.Clear(ClearBufferMask.ColorBufferBit);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
            var pixels = new byte[side * side * 3];
            GL.ReadPixels(origin, origin, side, side, PixelFormat.Rgb, PixelType.UnsignedByte, pixels);
            using var stream = File.Create(Path.Combine(directory, name + ".ppm"));
            stream.Write(System.Text.Encoding.ASCII.GetBytes($"P6\n{side} {side}\n255\n"));
            for (int y = side - 1; y >= 0; y--) stream.Write(pixels, y * side * 3, side * 3);
        }
        // Water preview uses the production simulation and shader. Render a
        // steady rainfall followed by sheltered evaporation, without any downward motion.
        var water = new LensRainModel();
        F("extinction", 0); F("glareStrength", 0);
        F("lensRainStrength", 1); F("lensLight", 1);
        float inset = 23.76f / side;
        GL.Uniform2(U("rainInset"), inset, inset);
        for (int tick = 0; tick < 600; tick++) water.Update(1f / 60, 1);
        for (int frame = 0; frame < 90; frame++)
        {
            for (int tick = 0; tick < 6; tick++) water.Update(1f / 60, frame < 45 ? 1 : 0);
            GL.Uniform1(U("rainDropCount"), water.Drops.Count);
            GL.Uniform4(U("rainDrops"), LensRainModel.MaxDrops, water.DropUniforms);
            GL.Uniform4(U("rainDropShapes"), LensRainModel.MaxDrops, water.DropShapeUniforms);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
            var pixels = new byte[side * side * 3];
            GL.ReadPixels(origin, origin, side, side, PixelFormat.Rgb, PixelType.UnsignedByte, pixels);
            using var stream = File.Create(Path.Combine(directory, $"water-{frame:D3}.ppm"));
            stream.Write(System.Text.Encoding.ASCII.GetBytes($"P6\n{side} {side}\n255\n"));
            for (int y = side - 1; y >= 0; y--) stream.Write(pixels, y * side * 3, side * 3);
        }
        // Uniform sky/fog and glare are deliberately included: foliage alone
        // hides the loss of surface contrast in the old shading.
        var denseBeads = new LensRainModel();
        for (int tick = 0; tick < 600; tick++) denseBeads.Update(1f / 60, 1);
        foreach (string background in new[] { "foliage", "fog", "sky", "sun", "chart" })
        {
            var backdrop = new byte[size * size * 4];
            if (background == "foliage") Array.Copy(input, backdrop, input.Length);
            else for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                int j = (y * size + x) * 4;
                float v = y / (float)(size - 1);
                float baseValue = background == "fog" ? .86f + .10f * v : background == "sun" ? .98f : .77f + .14f * v;
                backdrop[j] = (byte)(baseValue * 255);
                backdrop[j + 1] = (byte)(Math.Min(1, baseValue + (background == "sky" ? .045f : .01f)) * 255);
                backdrop[j + 2] = (byte)(Math.Min(1, baseValue + (background == "sky" ? .10f : .02f)) * 255);
                backdrop[j + 3] = 255;
                if (background == "chart")
                {
                    bool line = (x - origin) % 18 < 2 || (y - origin) % 18 < 2;
                    backdrop[j] = (byte)(line ? 55 : 194);
                    backdrop[j + 1] = (byte)(line ? 75 : 214);
                    backdrop[j + 2] = (byte)(line ? 85 : 224);
                }
            }
            GL.ActiveTexture(TextureUnit.Texture0); GL.BindTexture(TextureTarget.Texture2D, scene);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, size, size, 0, PixelFormat.Rgba, PixelType.UnsignedByte, backdrop);
            F("glareStrength", background == "sun" ? .20f : 0);
            F("extinction", 0); F("lensLight", 1);
            foreach (string effect in new[] { "clear", "beads" })
            {
                F("lensRainStrength", 1);
                GL.Uniform1(U("rainDropCount"), effect == "beads" ? denseBeads.Drops.Count : 0);
                GL.Uniform4(U("rainDrops"), LensRainModel.MaxDrops, denseBeads.DropUniforms);
                GL.Uniform4(U("rainDropShapes"), LensRainModel.MaxDrops, denseBeads.DropShapeUniforms);
                GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
                var pixels = new byte[side * side * 3];
                GL.ReadPixels(origin, origin, side, side, PixelFormat.Rgb, PixelType.UnsignedByte, pixels);
                using var stream = File.Create(Path.Combine(directory, $"visibility-{background}-{effect}.ppm"));
                stream.Write(System.Text.Encoding.ASCII.GetBytes($"P6\n{side} {side}\n255\n"));
                for (int y = side - 1; y >= 0; y--) stream.Write(pixels, y * side * 3, side * 3);
            }
        }
        // Measure the production pass in a hidden GL context, not the cost of
        // PNG/GIF encoding. This is an isolated GPU sample, not game FPS.
        F("lensRainStrength", 1);
        GL.Uniform1(U("rainDropCount"), denseBeads.Drops.Count);
        GL.Uniform4(U("rainDrops"), LensRainModel.MaxDrops, denseBeads.DropUniforms);
        GL.Uniform4(U("rainDropShapes"), LensRainModel.MaxDrops, denseBeads.DropShapeUniforms);
        int timer = GL.GenQuery();
        GL.BeginQuery(QueryTarget.TimeElapsed, timer);
        for (int i = 0; i < 20; i++) GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
        GL.EndQuery(QueryTarget.TimeElapsed);
        GL.GetQueryObject(timer, GetQueryObjectParam.QueryResult, out long elapsedNs);
        Console.WriteLine($"GPU preview sample: {denseBeads.Drops.Count} beads, {elapsedNs / 20.0 / 1000000.0:0.000} ms/pass at 1044x1044, 512x512 lens");
        GL.DeleteQuery(timer);
        var previewError = GL.GetError();
        if (previewError != ErrorCode.NoError) throw new InvalidOperationException("Preview OpenGL error: " + previewError);
        GL.DeleteFramebuffer(framebuffer);
        GL.DeleteTexture(scene); GL.DeleteTexture(depth); GL.DeleteTexture(output);
        Console.WriteLine("Rendered production shader preview: " + directory);
    }
}
