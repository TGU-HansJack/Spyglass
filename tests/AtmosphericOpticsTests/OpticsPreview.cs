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
        F("flickProgress", 1);
        GL.Uniform1(U("frostCount"), 0);
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
            GL.Uniform1(U("frostCount"), water.Crystals.Count);
            GL.Uniform4(U("rainDrops"), LensRainModel.MaxDrops, water.DropUniforms);
            GL.Uniform4(U("rainDropShapes"), LensRainModel.MaxDrops, water.DropShapeUniforms);
            GL.Uniform4(U("frostCrystals"), LensRainModel.MaxFrost, water.FrostUniforms);
            GL.Uniform4(U("frostDetails"), LensRainModel.MaxFrost, water.FrostDetailUniforms);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
            var pixels = new byte[side * side * 3];
            GL.ReadPixels(origin, origin, side, side, PixelFormat.Rgb, PixelType.UnsignedByte, pixels);
            using var stream = File.Create(Path.Combine(directory, $"water-{frame:D3}.ppm"));
            stream.Write(System.Text.Encoding.ASCII.GetBytes($"P6\n{side} {side}\n255\n"));
            for (int y = side - 1; y >= 0; y--) stream.Write(pixels, y * side * 3, side * 3);
        }
        var frost = new LensRainModel();
        // Skip the random waiting time, then preview the actual eight-second
        // edge growth rather than selecting an already finished crystal.
        for (int tick = 0; tick < 3600 && frost.Crystals.Count == 0; tick++) frost.Update(1f / 60, 0, true);
        GL.Uniform1(U("rainDropCount"), 0);
        F("lensRainStrength", 0);
        for (int frame = 0; frame < 90; frame++)
        {
            for (int tick = 0; tick < 6; tick++) frost.Update(1f / 60, 0, true);
            GL.Uniform1(U("frostCount"), frost.Crystals.Count);
            GL.Uniform4(U("frostCrystals"), LensRainModel.MaxFrost, frost.FrostUniforms);
            GL.Uniform4(U("frostDetails"), LensRainModel.MaxFrost, frost.FrostDetailUniforms);
            var uniformError = GL.GetError();
            if (uniformError != ErrorCode.NoError) throw new InvalidOperationException("Frost preview uniform error: " + uniformError);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
            var drawError = GL.GetError();
            if (drawError != ErrorCode.NoError) throw new InvalidOperationException("Frost preview draw error: " + drawError);
            var pixels = new byte[side * side * 3];
            GL.ReadPixels(origin, origin, side, side, PixelFormat.Rgb, PixelType.UnsignedByte, pixels);
            using var stream = File.Create(Path.Combine(directory, $"frost-{frame:D3}.ppm"));
            stream.Write(System.Text.Encoding.ASCII.GetBytes($"P6\n{side} {side}\n255\n"));
            for (int y = side - 1; y >= 0; y--) stream.Write(pixels, y * side * 3, side * 3);
        }
        var previewError = GL.GetError();
        if (previewError != ErrorCode.NoError) throw new InvalidOperationException("Preview OpenGL error: " + previewError);
        GL.DeleteFramebuffer(framebuffer);
        GL.DeleteTexture(scene); GL.DeleteTexture(depth); GL.DeleteTexture(output);
        Console.WriteLine("Rendered production shader preview: " + directory);
    }
}
