using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using spyglass.src.Client;

int checks = 0;
void Check(bool condition, string label)
{
    if (!condition) throw new Exception("FAIL " + label);
    checks++;
    Console.WriteLine("PASS " + label);
}
void Near(double actual, double expected, string label, double tolerance = 0.0001)
    => Check(Math.Abs(actual - expected) <= tolerance, $"{label}: {actual:0.######}");
float Rain(float intensity = 1, float strength = 1)
    => AtmosphericOpticsModel.Extinction(OpticsPrecipitation.Rain, intensity, strength);
Near(1 - Math.Exp(-Rain() * 256), .15, "full rain loses 15% contrast at 256 blocks");
Near(1 - Math.Exp(-AtmosphericOpticsModel.Extinction(OpticsPrecipitation.Snow, 1, 1) * 256), .25, "full snow loses 25% at 256 blocks");
Near(Rain(.5f), Rain() / 2, "actual precipitation intensity scales extinction");
Near(Rain(1, 2), Rain() * 2, "strength scales extinction");
Near(Rain(0), 0, "clear weather adds no attenuation");
Near(AtmosphericOpticsModel.LensRainIntensity(OpticsPrecipitation.Rain, .6f, 112, 110), .6f, "exposed lens follows actual rainfall");
Near(AtmosphericOpticsModel.LensRainIntensity(OpticsPrecipitation.Rain, 1, 112, 114), 0, "roof shelters the lens from rain impacts");
Near(AtmosphericOpticsModel.LensRainIntensity(OpticsPrecipitation.Rain, 0, 112, 110), 0, "dry weather produces no lens impacts");
Near(AtmosphericOpticsModel.LensRainIntensity(OpticsPrecipitation.Snow, 1, 112, 110), 0, "snow does not produce liquid rain impacts");
Near(AtmosphericOpticsModel.LensRainIntensity(OpticsPrecipitation.Hail, 1, 112, 110), 0, "hail does not produce liquid rain impacts");
Near(AtmosphericOpticsModel.LensRainIntensity(OpticsPrecipitation.Rain, 1, double.NaN, 110), 0, "invalid camera height cannot enable rain impacts");
Near(AtmosphericOpticsModel.Extinction(OpticsPrecipitation.Hail, 1, 1), Rain(), "hail uses rain optics");
Near(AtmosphericOpticsModel.Extinction(OpticsPrecipitation.None, 1, 1), 0, "no precipitation adds no attenuation");
Check(AtmosphericOpticsModel.ResolvePrecipitation(OpticsPrecipitation.Auto, -1, 0) == OpticsPrecipitation.Snow
    && AtmosphericOpticsModel.ResolvePrecipitation(OpticsPrecipitation.Auto, 1, 0) == OpticsPrecipitation.Rain,
    "automatic rain/snow follows actual temperature threshold");
Check(AtmosphericOpticsModel.ResolvePrecipitation(OpticsPrecipitation.Rain, -10, 0) == OpticsPrecipitation.Rain,
    "explicit weather type wins over temperature");
Near(AtmosphericOpticsModel.Strength(float.NaN), 1, "invalid strength uses default");
Near(AtmosphericOpticsModel.Strength(-1), 0, "negative strength disabled");
Near(AtmosphericOpticsModel.Strength(5), 2, "strength capped at two");
Near(AtmosphericOpticsModel.Approach(0, 1, float.NaN), 0, "invalid delta time is safe");
Near(AtmosphericOpticsModel.Approach(AtmosphericOpticsModel.Approach(0, 1, .1f), 1, .1f),
    AtmosphericOpticsModel.Approach(0, 1, .2f), "weather smoothing independent of frame subdivision");
Near(AtmosphericOpticsModel.Glare(1, 1, 0, 0, 1), .1, "default maximum glare ten percent");
Near(AtmosphericOpticsModel.Glare(1, 1, 0, 0, 2), .2, "strong maximum glare twenty percent");
Near(AtmosphericOpticsModel.Glare(-.1f, 1, 0, 0, 1), 0, "no night sun glare");
Near(AtmosphericOpticsModel.Glare(1, 0, 0, 0, 1), 0, "engine sun occlusion suppresses glare");
Near(AtmosphericOpticsModel.Glare(1, 1, 1, 0, 1), 0, "dense clouds suppress glare");
Check(AtmosphericOpticsModel.Glare(1, 1, 0, .05f, 1) < .01f, "dense fog suppresses glare");
foreach (var (w, h) in new[] { (1920, 1080), (720, 1280), (800, 800) })
{
    var a = LensAperture.Square(0, 0, w, h, 10, .5f);
    Near(a.Width * a.Height / Math.Pow(Math.Min(w, h) - 20, 2), .25, $"quarter area at {w}x{h}");
    Check(a.Width == a.Height && a.X + a.Width / 2 == w / 2d && a.Y + a.Height / 2 == h / 2d,
        $"square centered at {w}x{h}");
}
Near(LensAperture.Square(0, 0, 100, 100, 100, .5f).Width, 0, "tiny window cannot produce negative aperture");
Near(LensAperture.Square(0, 0, 100, 100, -100, .5f).Width, 50, "negative margin cannot exceed viewport");

// Standard OpenGL projection, column-major, matching the game's matrix storage.
double[] Projection(double fov, double near, double far, double aspect = 1)
{
    double f = 1 / Math.Tan(fov / 2);
    return [f / aspect, 0, 0, 0, 0, f, 0, 0, 0, 0, -(far + near) / (far - near), -1,
        0, 0, -2 * far * near / (far - near), 0];
}
float[] Inverse(double[] projection)
{
    var result = new float[16];
    Check(AtmosphericOpticsModel.InvertProjection(projection, result), "projection invertible");
    return result;
}
foreach (var (fov, near, far) in new[] { (1.2, .1, 2048d), (.12, .01, 1024d), (.8, .5, 4096d) })
{
    var p = Projection(fov, near, far, 16d / 9);
    var inv = Inverse(p);
    double[] point = [15, -8, -256, 1];
    double[] Mul(double[] m, double[] v) => Enumerable.Range(0, 4).Select(r => Enumerable.Range(0, 4).Sum(c => m[c * 4 + r] * v[c])).ToArray();
    var clip = Mul(p, point);
    var restored = Mul(inv.Select(x => (double)x).ToArray(), clip);
    for (int i = 0; i < 3; i++) Near(restored[i] / restored[3], point[i], $"view point reconstruction fov={fov} axis={i}", .1);
}
Check(!AtmosphericOpticsModel.InvertProjection(new double[16], new float[16]), "singular projection rejected");

var merging = new LensRainModel();
merging.AddDrop(.5f, .3f, .012f); merging.AddDrop(.51f, .3f, .012f);
merging.Update(1f / 60, 0);
Check(merging.Drops.Count == 1 && merging.Drops[0].Radius > .016f, "touching beads merge into a larger drop");
float initialY = merging.Drops[0].Y;
for (int i = 0; i < 180; i++) merging.Update(1f / 60, 0);
Check(merging.Drops.Count == 1 && merging.Drops[0].Y > initialY + .1f, "large beads slide down under gravity");
Check(merging.Trails.Count > 0, "sliding beads leave persistent water trails");
Check(merging.Trails.Any(t => t.WidthTop > 0 && t.WidthTop < .02f && t.WidthBottom != t.WidthTop), "water trails vary in pixel width");
Check(merging.Trails.Any(t => Math.Abs(t.XBottom - t.XTop) > .0001f) || merging.Drops.Any(d => Math.Abs(d.Drift) > .001f),
    "water paths are not locked to a single vertical route");
Check(merging.Drops.Any(d => d.Pause > 0) || merging.Trails.Any(t => t.Life < 2.5f),
    "water motion includes pauses and irregular trail lifetimes");
for (int i = 0; i < 600; i++) merging.Update(1f / 60, 0);
Check(merging.Drained > 0, "runoff reaches the bottom and drains away");
var wet = new LensRainModel();
for (int i = 0; i < 1800; i++) wet.Update(1f / 60, 1);
Check(wet.HasWater && wet.Pool > .005f && wet.Pool <= .055f, "sustained rain accumulates bounded water at the bottom");
Check(wet.Drops.Count <= LensRainModel.MaxDrops && wet.Trails.Count <= LensRainModel.MaxTrails, "water simulation has bounded drop and trail counts");
wet.Update(1f / 60, 0);
Check(wet.HasWater, "entering shelter stops new rain without erasing existing water");
for (int i = 0; i < 7200; i++) wet.Update(1f / 60, 0);
Check(!wet.HasWater, "sheltered lens dries after runoff and evaporation");
var rate30 = new LensRainModel(); var rate60 = new LensRainModel();
for (int i = 0; i < 600; i++) rate30.Update(1f / 30, 1);
for (int i = 0; i < 1200; i++) rate60.Update(1f / 60, 1);
Check(rate30.DropUniforms.SequenceEqual(rate60.DropUniforms) && rate30.Pool == rate60.Pool,
    "fixed-step water simulation agrees at 30 and 60 FPS");
rate30.Clear(); rate30.Update(1f / 60, 0);
Check(!rate30.HasWater && rate30.DropUniforms.All(v => v == 0), "lens reset clears water and uploaded drop data");

using var window = new NativeWindow(new NativeWindowSettings {
    ClientSize = new Vector2i(65, 65), StartVisible = false, StartFocused = false,
    API = ContextAPI.OpenGL, APIVersion = new Version(3, 3), Profile = ContextProfile.Core,
    Flags = ContextFlags.ForwardCompatible, Title = "Spyglass optics regression"
});
window.Context.MakeCurrent();
// Game uses the OpenGL bindings (rather than OpenGL4).
GL.LoadBindings(new OpenTK.Windowing.GraphicsLibraryFramework.GLFWBindingsContext());
Console.WriteLine("GPU " + GL.GetString(StringName.Renderer));
int Compile(ShaderType type, string path)
{
    int id = GL.CreateShader(type);
    string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "shaders", path));
    GL.ShaderSource(id, source);
    GL.CompileShader(id);
    GL.GetShader(id, ShaderParameter.CompileStatus, out int status);
    Check(status != 0, "actual " + type + " compiles: " + GL.GetShaderInfoLog(id));
    return id;
}
int vertex = Compile(ShaderType.VertexShader, "spyglassforkatmosphere.vsh");
int fragment = Compile(ShaderType.FragmentShader, "spyglassforkatmosphere.fsh");
int program = GL.CreateProgram();
GL.AttachShader(program, vertex); GL.AttachShader(program, fragment); GL.LinkProgram(program);
GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
Check(linked != 0, "actual optics shader links: " + GL.GetProgramInfoLog(program));
GL.DeleteShader(vertex); GL.DeleteShader(fragment);
GL.UseProgram(program);
int U(string name) => GL.GetUniformLocation(program, name);
void F(string name, float value) => GL.Uniform1(U(name), value);
int Texture(int size, PixelInternalFormat format, PixelFormat pixelFormat, float[] pixels)
{
    int id = GL.GenTexture(); GL.BindTexture(TextureTarget.Texture2D, id);
    GL.TexImage2D(TextureTarget.Texture2D, 0, format, size, size, 0, pixelFormat, PixelType.Float, pixels);
    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
    return id;
}
const int Size = 65;
int scene = Texture(1, PixelInternalFormat.Rgba32f, PixelFormat.Rgba, [.2f, .2f, .2f, .7f]);
int depthTexture = Texture(1, PixelInternalFormat.R32f, PixelFormat.Red, [.5f]);
int output = Texture(Size, PixelInternalFormat.Rgba32f, PixelFormat.Rgba, new float[Size * Size * 4]);
int framebuffer = GL.GenFramebuffer(); GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, output, 0);
Check(GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) == FramebufferErrorCode.FramebufferComplete, "test framebuffer complete");
int vao = GL.GenVertexArray(), vbo = GL.GenBuffer();
GL.BindVertexArray(vao); GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
float[] vertices = [-1, -1, 0, 1, -1, 0, 1, 1, 0, -1, -1, 0, 1, 1, 0, -1, 1, 0];
GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);
GL.EnableVertexAttribArray(0); GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 12, 0);
GL.Viewport(0, 0, Size, Size); GL.Disable(EnableCap.DepthTest); GL.Disable(EnableCap.Blend);
GL.Uniform1(U("sceneTexture"), 0); GL.Uniform1(U("depthTexture"), 1);
GL.Uniform2(U("frameSize"), (float)Size, (float)Size);
GL.Uniform3(U("fogColor"), .8f, .8f, .8f);
GL.Uniform4(U("colorGrade"), 1f, 1f, 1f, 0f); F("sepia", 0);
GL.Uniform2(U("sunScreen"), .5f, .5f); GL.Uniform3(U("sunViewDirection"), 0f, 0f, -1f);
var projection = Projection(1.2, .1, 4096);
GL.UniformMatrix4(U("inverseProjection"), 1, false, Inverse(projection));
void Depth(float distance)
{
    float d = distance == float.PositiveInfinity ? 1 : (float)((projection[10] * -distance + projection[14]) / distance * .5 + .5);
    GL.ActiveTexture(TextureUnit.Texture1); GL.BindTexture(TextureTarget.Texture2D, depthTexture);
    GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, 1, 1, PixelFormat.Red, PixelType.Float, new[] { d });
}
float[] Render(float distance = 256, float coefficient = -1, float day = 1, float glare = 0, float fade = 1, bool square = false)
{
    Depth(distance);
    GL.ActiveTexture(TextureUnit.Texture0); GL.BindTexture(TextureTarget.Texture2D, scene);
    F("extinction", coefficient < 0 ? Rain() : coefficient); F("dayWeight", day); F("glareStrength", glare); F("fade", fade);
    GL.Uniform4(U("aperture"), square ? .25f : 0, square ? .25f : 0, square ? .5f : 1, square ? .5f : 1);
    GL.ClearColor(.031f, .031f, .031f, 1); GL.Clear(ClearBufferMask.ColorBufferBit);
    GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
    var pixels = new float[Size * Size * 4];
    GL.ReadPixels(0, 0, Size, Size, PixelFormat.Rgba, PixelType.Float, pixels);
    return pixels;
}
float Pixel(float[] pixels, int x = 32, int y = 32) => pixels[(y * Size + x) * 4];
Near(Pixel(Render()), .29, "GPU rain attenuation at 256 blocks", .0005);
Near(Pixel(Render(coefficient: AtmosphericOpticsModel.Extinction(OpticsPrecipitation.Snow, 1, 1))), .35, "GPU snow attenuation at 256 blocks", .0005);
Check(Pixel(Render(32)) < Pixel(Render(256)), "GPU distant surfaces lose more contrast");
Check(Pixel(Render(), 60, 32) > Pixel(Render()), "GPU off-axis pixels use radial distance, not just view depth");
Near(Pixel(Render(3000, Rain(1, 2))), .38, "GPU additional weather blend capped at thirty percent", .0005);
Near(Pixel(Render(float.PositiveInfinity)), .2, "GPU sky retains engine fog");
Near(Pixel(Render(day: 0)), .2, "GPU full night never brightens dark target");
GL.Uniform4(U("colorGrade"), 1.8f, 1.2f, 1.4f, .1f); F("sepia", .3f);
Near(Pixel(Render(day: 0)), .2, "GPU night clamp remains effective with non-default color grading");
GL.Uniform4(U("colorGrade"), 1f, 1f, 1f, 0f); F("sepia", 0);
GL.Uniform3(U("fogColor"), .05f, .05f, .05f);
Check(Pixel(Render(day: 0)) < .2, "GPU night scattering may darken bright targets");
GL.Uniform3(U("fogColor"), .8f, .8f, .8f);
Near(Pixel(Render(coefficient: 0)), .2, "GPU disabled weather preserves scene");
Near(Pixel(Render(fade: 0)), .2, "GPU zero zoom fade preserves scene");
Near(Pixel(Render(fade: .5f)), .245, "GPU optics fades with zoom animation", .0005);
var cropped = Render(square: true);
Near(Pixel(cropped, 2, 2), .031, "GPU does not draw outside square opening");
Near(Pixel(cropped), .29, "GPU square opening receives weather", .0005);
var halo = Render(coefficient: 0, glare: .1f);
Near(Pixel(halo), .28, "GPU local sun halo strength", .0005);
Near(Pixel(Render(coefficient: 0, glare: .5f)), .36, "GPU sun halo capped at twenty percent", .0005);
GL.Uniform2(U("frameSize"), Size * 2f, Size * 2f);
Near(Pixel(Render(coefficient: 0, glare: .1f), 35, 32), Pixel(halo, 35, 32), "GPU halo size follows physical aperture across render scales");
GL.Uniform2(U("frameSize"), (float)Size, (float)Size);
Check(Pixel(halo, 10, 10) < .201, "GPU halo does not whiten entire view");
GL.Uniform2(U("sunScreen"), -.5f, -.5f);
Near(Pixel(Render(coefficient: 0, glare: .1f, square: true)), .2, "GPU offscreen sun halo disappears");
GL.Uniform2(U("sunScreen"), .5f, .5f);
// A zero glare weight from night/occlusion must leave all pixels unchanged.
Near(Pixel(Render(coefficient: 0, glare: AtmosphericOpticsModel.Glare(1, 0, 0, 0, 1))), .2, "GPU engine-occluded sun contributes no glare");
Near(halo[(32 * Size + 32) * 4 + 3], .7, "GPU scene alpha preserved");

var waterModel = new LensRainModel();
waterModel.AddDrop(.5f, .5f, .08f);
waterModel.Update(1f / 60, 0);
void UploadWater(LensRainModel model)
{
    GL.Uniform1(U("rainDropCount"), model.Drops.Count);
    GL.Uniform1(U("rainTrailCount"), model.Trails.Count);
    GL.Uniform4(U("rainDrops"), LensRainModel.MaxDrops, model.DropUniforms);
    GL.Uniform4(U("rainTrails"), LensRainModel.MaxTrails * 2, model.TrailUniforms);
    F("rainPool", model.Pool);
}
F("lensRainStrength", 1); F("lensLight", 1);
UploadWater(waterModel);
var beads = Render(coefficient: 0);
Check(Pixel(beads) < .2f, "GPU water beads have filled centers rather than hollow rings");
Near(Pixel(beads, 2, 2), .2, "GPU dry glass outside the bead is unchanged");
for (int i = 0; i < 30; i++) waterModel.Update(1f / 60, 0);
UploadWater(waterModel);
Check(!beads.SequenceEqual(Render(coefficient: 0)), "GPU water follows simulated downward motion");
Near(beads[(32 * Size + 32) * 4 + 3], .7, "GPU water preserves scene alpha");
F("lensRainStrength", 0);
var disabledRain = Render(coefficient: 0);
Check(Enumerable.Range(0, Size * Size).All(i => Math.Abs(disabledRain[i * 4] - .2f) < .0001f), "GPU zero lens rain strength preserves the scene");
F("lensRainStrength", 1);
Check(disabledRain.SequenceEqual(Render(coefficient: 0, fade: 0)), "GPU water fades out with zoom");
Near(Pixel(Render(coefficient: 0, square: true), 2, 2), .031, "GPU water stays inside the lens");
waterModel.Clear(); waterModel.Update(1f / 60, 0); UploadWater(waterModel);
Check(disabledRain.SequenceEqual(Render(coefficient: 0)), "GPU dry lens has no residual effect");
// Isolate a vertical residual stream and the bottom meniscus.
GL.Uniform1(U("rainTrailCount"), 1);
GL.Uniform4(U("rainTrails"), .5f, .5f, .1f, .9f);
GL.Uniform4(U("rainTrails[1]"), .012f, .008f, 1f, .62f);
Check(Pixel(Render(coefficient: 0)) < .2f, "GPU residual streams shade a vertical water path");
GL.Uniform1(U("rainTrailCount"), 0); F("rainPool", .04f);
var pooled = Render(coefficient: 0);
Check(Math.Min(Pixel(pooled, 32, 0), Pixel(pooled, 32, 60)) < .2f, "GPU accumulated water occupies the bottom edge");
Check(Math.Max(Pixel(pooled, 32, 0), Pixel(pooled, 32, 60)) > .199f, "GPU bottom water does not flood the upper lens");
F("rainPool", 0); F("lensRainStrength", 0);

GL.Enable(EnableCap.DepthTest); GL.Enable(EnableCap.Blend); GL.Enable(EnableCap.CullFace); GL.DepthMask(true);
GL.BlendFuncSeparate(BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha, BlendingFactorSrc.One, BlendingFactorDest.Zero);
int sampler = GL.GenSampler(); GL.BindSampler(0, sampler);
GL.ActiveTexture(TextureUnit.Texture3);
try
{
    using var state = new OpticsRenderState();
    GL.Disable(EnableCap.DepthTest); GL.Disable(EnableCap.Blend); GL.Disable(EnableCap.CullFace); GL.DepthMask(false);
    GL.BlendFunc(BlendingFactor.One, BlendingFactor.One);
    GL.UseProgram(0); GL.BindVertexArray(0); GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
    GL.ActiveTexture(TextureUnit.Texture0); GL.BindTexture(TextureTarget.Texture2D, 0); GL.BindSampler(0, 0);
    GL.ActiveTexture(TextureUnit.Texture1); GL.BindTexture(TextureTarget.Texture2D, 0);
    throw new InvalidOperationException("simulated draw failure");
}
catch (InvalidOperationException) { }
Check(GL.IsEnabled(EnableCap.DepthTest) && GL.IsEnabled(EnableCap.Blend) && GL.IsEnabled(EnableCap.CullFace)
    && GL.GetBoolean(GetPName.DepthWritemask), "GL state restored after a draw exception");
Check(GL.GetInteger(GetPName.CurrentProgram) == program && GL.GetInteger(GetPName.VertexArrayBinding) == vao
    && GL.GetInteger(GetPName.ArrayBufferBinding) == vbo, "GL shader and geometry bindings restored");
Check(GL.GetInteger(GetPName.BlendSrcRgb) == (int)BlendingFactorSrc.SrcAlpha
    && GL.GetInteger(GetPName.BlendDstRgb) == (int)BlendingFactorDest.OneMinusSrcAlpha
    && GL.GetInteger(GetPName.BlendSrcAlpha) == (int)BlendingFactorSrc.One
    && GL.GetInteger(GetPName.BlendDstAlpha) == (int)BlendingFactorDest.Zero, "GL separate blend factors restored after lens drawing");
Check(GL.GetInteger(GetPName.ActiveTexture) == (int)TextureUnit.Texture3, "GL active texture unit restored");
GL.ActiveTexture(TextureUnit.Texture0);
Check(GL.GetInteger(GetPName.TextureBinding2D) == scene && GL.GetInteger(GetPName.SamplerBinding) == sampler,
    "GL scene texture and sampler restored");
GL.ActiveTexture(TextureUnit.Texture1);
Check(GL.GetInteger(GetPName.TextureBinding2D) == depthTexture, "GL depth texture restored");
Check(GL.GetError() == ErrorCode.NoError, "no OpenGL errors");
if (args.Length == 2 && args[0] == "--preview")
    OpticsPreview.Render(program, vao, Path.GetFullPath(args[1]));
GL.DeleteSampler(sampler); GL.DeleteBuffer(vbo); GL.DeleteVertexArray(vao); GL.DeleteFramebuffer(framebuffer);
GL.DeleteTexture(scene); GL.DeleteTexture(depthTexture); GL.DeleteTexture(output); GL.DeleteProgram(program);
Console.WriteLine($"SUCCESS {checks} assertions");
