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

var stationary = new LensRainModel();
stationary.AddDrop(.2f, .25f, .008f);
stationary.AddDrop(.5f, .55f, .015f);
stationary.AddDrop(.8f, .90f, .022f);
var positions = stationary.Drops.Select(d => (d.X, d.Y, d.Radius)).ToArray();
for (int i = 0; i < 600; i++) stationary.Update(1f / 60, 0);
Check(stationary.Drops.Select(d => (d.X, d.Y, d.Radius)).SequenceEqual(positions),
    "all bead sizes remain fixed in place without gravity or position-based growth");
Check(stationary.Drops.All(d => d.Fade > .7f && d.Fade < 1), "stationary beads evaporate through opacity rather than flattening");
var merging = new LensRainModel();
merging.AddDrop(.5f, .5f, .021f); merging.AddDrop(.51f, .5f, .021f);
merging.Update(1f / 60, 0);
Check(merging.Drops.Count == 1 && merging.Drops[0].Radius == LensRainModel.MaxDropRadius,
    "only touching beads merge and size is capped");
Check(merging.Drops[0].X == .5f && merging.Drops[0].Y == .5f, "merging does not make beads crawl");
var wet = new LensRainModel();
for (int i = 0; i < 1800; i++) wet.Update(1f / 60, 1);
Check(wet.HasWater && wet.Drops.Count <= LensRainModel.MaxDrops, "rain leaves a bounded set of surface beads");
Check(wet.Drops.Count >= 45, "sustained rain maintains dozens of stationary beads rather than a sparse dozen");
Check(wet.Drops.Count(d => d.Radius < .012f) >= 15, "dense rainfall retains numerous small beads instead of giant merged spots");
for (int i = 0; i < 3600; i++) wet.Update(1f / 60, 0);
Check(!wet.HasWater, "surface beads evaporate within a minute after rain stops");
var frost = new LensRainModel();
for (int i = 0; i < 2400; i++) frost.Update(1f / 60, 0, true);
Check(frost.Crystals.Count is > 0 and <= LensRainModel.MaxFrost, "cold exposure forms at most two frost patches");
Check(frost.Crystals.All(c => c.X is .012f or .988f || c.Y is .012f or .988f), "frost is anchored at a lens edge");
Check(frost.Crystals.All(c => c.Radius >= .34f && c.Radius <= .41f), "grown frost extends at least one sixth of the full lens");
Check(frost.Crystals.All(c => c.Growth > .9f), "frost grows from the edge over time");
for (int i = 0; i < 900; i++) frost.Update(1f / 60, 0, false);
Check(frost.Crystals.Count == 0, "warm exposure melts frost");
var warm = new LensRainModel();
for (int i = 0; i < 1200; i++) warm.Update(1f / 60, 0, false);
Check(!warm.HasWater, "warm dry glass generates neither frost nor beads");
var rate30 = new LensRainModel(); var rate60 = new LensRainModel();
for (int i = 0; i < 600; i++) rate30.Update(1f / 30, 1, true);
for (int i = 0; i < 1200; i++) rate60.Update(1f / 60, 1, true);
Check(rate30.DropUniforms.SequenceEqual(rate60.DropUniforms) && rate30.FrostDetailUniforms.SequenceEqual(rate60.FrostDetailUniforms),
    "beads and edge growth agree at 30 and 60 FPS");
rate30.Clear();
Check(!rate30.HasWater && rate30.DropUniforms.All(v => v == 0) && rate30.FrostUniforms.All(v => v == 0),
    "flick clears all surface states and uploaded data");

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
GL.ActiveTexture(TextureUnit.Texture2);
int frostTexture = GL.GenTexture(); GL.BindTexture(TextureTarget.Texture2D, frostTexture);
byte[] frostImage = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "frost.rgba"));
GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, 512, 512, 0, PixelFormat.Rgba, PixelType.UnsignedByte, frostImage);
GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
GL.BindSampler(2, 0); GL.Uniform1(U("frostTexture"), 2);
GL.ActiveTexture(TextureUnit.Texture0);
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
F("flickProgress", 1);
GL.Uniform1(U("frostCount"), 0);
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
    GL.Uniform1(U("frostCount"), model.Crystals.Count);
    GL.Uniform4(U("rainDrops"), LensRainModel.MaxDrops, model.DropUniforms);
    GL.Uniform4(U("rainDropShapes"), LensRainModel.MaxDrops, model.DropShapeUniforms);
    GL.Uniform4(U("frostCrystals"), LensRainModel.MaxFrost, model.FrostUniforms);
    GL.Uniform4(U("frostDetails"), LensRainModel.MaxFrost, model.FrostDetailUniforms);
}
F("lensRainStrength", 1); F("lensLight", 1);
UploadWater(waterModel);
var beads = Render(coefficient: 0);
Check(Pixel(beads) < .2f, "GPU water beads have filled centers rather than hollow rings");
var frostModel = new LensRainModel();
for (int i = 0; i < 2400; i++) frostModel.Update(1f / 60, 0, true);
UploadWater(frostModel);
var frostPixels = Render(coefficient: 0);
Check(frostModel.Crystals.Count > 0 && frostPixels.Any(v => v > .2f), "GPU renders sparse crystalline frost");
frostModel.Clear(); UploadWater(frostModel);
Near(Pixel(beads, 2, 2), .2, "GPU dry glass outside the bead is unchanged");
for (int i = 0; i < 30; i++) waterModel.Update(1f / 60, 0);
UploadWater(waterModel);
Check(!beads.SequenceEqual(Render(coefficient: 0)), "GPU bead opacity changes during evaporation");
Near(beads[(32 * Size + 32) * 4 + 3], .7, "GPU water preserves scene alpha");
F("lensRainStrength", 0);
var disabledRain = Render(coefficient: 0);
Check(Enumerable.Range(0, Size * Size).All(i => Math.Abs(disabledRain[i * 4] - .2f) < .0001f), "GPU zero lens rain strength preserves the scene");
F("lensRainStrength", 1);
Check(disabledRain.SequenceEqual(Render(coefficient: 0, fade: 0)), "GPU water fades out with zoom");
Near(Pixel(Render(coefficient: 0, square: true), 2, 2), .031, "GPU water stays inside the lens");
waterModel.Clear(); waterModel.Update(1f / 60, 0); UploadWater(waterModel);
Check(disabledRain.SequenceEqual(Render(coefficient: 0)), "GPU dry lens has no residual effect");
// Empty glass must have no phantom trails or water along its lower edge.
GL.Uniform1(U("frostCount"), 0);
var emptySurface = Render(coefficient: 0);
Check(Enumerable.Range(0, Size * Size).All(i => Math.Abs(emptySurface[i * 4] - .2f) < .0001f),
    "GPU clear surface has no water trails or bottom water plane");
var onlyBead = new LensRainModel(); onlyBead.AddDrop(.5f, .5f, .02f); onlyBead.Update(1f / 60, 0);
UploadWater(onlyBead);
var isolated = Render(coefficient: 0);
Check(Enumerable.Range(0, Size).All(x => Math.Abs(Pixel(isolated, x, 0) - .2f) < .0001f),
    "GPU bead creates no bottom accumulation");
Near(Pixel(isolated, 32, 10), .2f, "GPU isolated bead leaves no vertical tail");
onlyBead.Clear(); UploadWater(onlyBead);
// A deterministic left-edge patch tests visibility and growth, not just any
// changed output pixel (alpha must not count as a visible frost pixel).
GL.Uniform1(U("frostCount"), 1);
GL.Uniform4(U("frostCrystals"), .012f, .5f, .30f, 1f);
GL.Uniform4(U("frostDetails"), 0f, 0f, .4f, 0f);
var unborn = Render(coefficient: 0);
Check(Enumerable.Range(0, Size * Size).All(i => Math.Abs(unborn[i * 4] - .2f) < .0001f), "GPU frost starts with bare glass");
GL.Uniform4(U("frostDetails"), 0f, .35f, .4f, 0f);
var growing = Render(coefficient: 0);
GL.Uniform4(U("frostDetails"), 0f, 1f, .4f, 0f);
var grown = Render(coefficient: 0);
int Coverage(float[] px) => Enumerable.Range(0, Size * Size).Count(i => px[i * 4] > .205f);
Check(Coverage(grown) > Coverage(growing) && Coverage(growing) > 0, "GPU frost branches grow inward instead of fading in all at once");
int farthest = Enumerable.Range(0, Size * Size).Where(i => grown[i * 4] > .205f).Max(i => i % Size);
Check(farthest >= Size / 6, "GPU frost patch visibly spans at least a sixth of the lens");
Check(grown.Where((_, i) => i % 4 == 0).Max() > .205f, "GPU microfacets catch light along ice ridges");
Check(grown.Where((_, i) => i % 4 == 0).Max() < .50f, "GPU ice transmits most scene color rather than a white painted texture");
Check(Coverage(grown) < Size * Size / 10, "GPU frost has transparent gaps and does not create an opaque sheet");
Near(Pixel(grown, 40, 32), .2f, "GPU edge frost leaves the center view clear");
GL.Uniform4(U("frostDetails"), 0f, .35f, .4f, 0f);
var growingAgain = Render(coefficient: 0);
Check(growing.SequenceEqual(growingAgain), "GPU partially grown frost has stable branches without flicker");
// Refraction must still alter a patterned scene when illumination is zero;
// this distinguishes transmitted background displacement from white alpha paint.
GL.ActiveTexture(TextureUnit.Texture0); GL.BindTexture(TextureTarget.Texture2D, scene);
var pattern = new float[Size * Size * 4];
for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
{
    int j = (y * Size + x) * 4;
    pattern[j] = pattern[j + 1] = pattern[j + 2] = ((x / 3 + y / 3) % 2 == 0) ? .1f : .7f;
    pattern[j + 3] = .7f;
}
GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba32f, Size, Size, 0, PixelFormat.Rgba, PixelType.Float, pattern);
F("lensLight", 0);
GL.Uniform1(U("frostCount"), 0); var plainPattern = Render(coefficient: 0);
GL.Uniform1(U("frostCount"), 1);
GL.Uniform4(U("frostDetails"), 0f, 1f, .4f, 0f);
var refractedPattern = Render(coefficient: 0);
Check(Enumerable.Range(0, Size * Size).Any(i => Math.Abs(refractedPattern[i * 4] - plainPattern[i * 4]) > .025f),
    "GPU frost refracts a patterned background even with reflection unlit");
GL.Uniform1(U("frostCount"), 0);
var patternBead = new LensRainModel(); patternBead.AddDrop(.5f, .5f, .024f); patternBead.Update(1f / 60, 0);
UploadWater(patternBead); F("lensRainStrength", 1);
var beadPattern = Render(coefficient: 0);
Check(Enumerable.Range(0, Size * Size).Any(i => Math.Abs(beadPattern[i * 4] - plainPattern[i * 4]) > .08f),
    "GPU optical bead actually magnifies or displaces background detail");
patternBead.Clear(); UploadWater(patternBead);
GL.Uniform1(U("frostCount"), 1);
GL.Uniform4(U("frostCrystals"), .012f, .5f, .30f, 1f);
GL.Uniform4(U("frostDetails"), 0f, 1f, .4f, 0f);
GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba32f, 1, 1, 0, PixelFormat.Rgba, PixelType.Float, new[] {0f, 0f, 0f, .7f});
var unlitIce = Render(coefficient: 0);
Check(Enumerable.Range(0, Size * Size).All(i => unlitIce[i * 4] == 0), "GPU unlit ice does not paint light onto a black background");
GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba32f, 1, 1, 0, PixelFormat.Rgba, PixelType.Float, new[] {.2f, .2f, .2f, .7f});
F("lensLight", 1);
GL.Uniform1(U("frostCount"), 0);
F("lensRainStrength", 0);

// Bright sky contains no detail for refraction to displace. Test RGB contrast
// against the background itself, not alpha or against a dark foliage screenshot.
void SolidScene(float value)
{
    GL.ActiveTexture(TextureUnit.Texture0); GL.BindTexture(TextureTarget.Texture2D, scene);
    GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba32f, 1, 1, 0, PixelFormat.Rgba, PixelType.Float,
        new[] {value, value, value, .7f});
}
foreach (float background in new[] { .82f, .95f, 1f })
{
    SolidScene(background);
    UploadWater(onlyBead);
    // onlyBead was cleared above; populate a fresh stationary dome.
    var brightBead = new LensRainModel(); brightBead.AddDrop(.5f, .5f, .024f); brightBead.Update(1f / 60, 0);
    UploadWater(brightBead); F("lensRainStrength", 1); F("lensLight", 1);
    var skyBead = Render(float.PositiveInfinity, coefficient: 0);
    Check(Enumerable.Range(0, Size * Size).Any(i => skyBead[i * 4] < background - .08f),
        $"GPU bead contact rim remains visible on uniform bright sky {background}");
    var sunlitBead = Render(float.PositiveInfinity, coefficient: 0, glare: .2f);
    Check(Enumerable.Range(0, Size * Size).Any(i => sunlitBead[i * 4] < background - .06f),
        $"GPU solar glare does not wash out bead contrast on sky {background}");
    Near(Pixel(skyBead, 2, 2), background, "GPU bead rim does not tint the surrounding bright sky");
    Near(skyBead[(32 * Size + 32) * 4 + 3], .7f, "GPU bright-sky bead preserves scene alpha");
    brightBead.Clear(); UploadWater(brightBead);
    GL.Uniform1(U("frostCount"), 1);
    GL.Uniform4(U("frostCrystals"), .012f, .5f, .38f, 1f);
    GL.Uniform4(U("frostDetails"), 0f, 1f, .4f, 0f);
    var skyIce = Render(float.PositiveInfinity, coefficient: 0, glare: .2f);
    Check(Enumerable.Range(0, Size * Size).Count(i => skyIce[i * 4] < background - .025f) >= 8,
        $"GPU feathered ice has visible dark facet boundaries on bright sky {background}");
    Check(Enumerable.Range(0, Size * Size).All(i => skyIce[i * 4] >= background * .50f),
        "GPU frost retains transmission on a bright uniform background");
    GL.Uniform1(U("frostCount"), 0);
}
SolidScene(0); UploadWater(onlyBead);
onlyBead.AddDrop(.5f, .5f, .024f); onlyBead.Update(1f / 60, 0); UploadWater(onlyBead);
F("lensLight", 0);
var unlitBead = Render(coefficient: 0);
Check(Enumerable.Range(0, Size * Size).All(i => unlitBead[i * 4] == 0), "GPU denser optical beads never self-illuminate black glass");
onlyBead.Clear(); UploadWater(onlyBead);
SolidScene(.2f); F("lensLight", 1); F("lensRainStrength", 0);

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
    GL.ActiveTexture(TextureUnit.Texture2); GL.BindTexture(TextureTarget.Texture2D, 0); GL.BindSampler(2, sampler);
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
GL.ActiveTexture(TextureUnit.Texture2);
Check(GL.GetInteger(GetPName.TextureBinding2D) == frostTexture && GL.GetInteger(GetPName.SamplerBinding) == 0,
    "GL frost texture and sampler restored on the third texture unit");
Check(GL.GetError() == ErrorCode.NoError, "no OpenGL errors");
if (args.Length == 2 && args[0] == "--preview")
    OpticsPreview.Render(program, vao, Path.GetFullPath(args[1]));
GL.DeleteSampler(sampler); GL.DeleteBuffer(vbo); GL.DeleteVertexArray(vao); GL.DeleteFramebuffer(framebuffer);
GL.DeleteTexture(frostTexture); GL.DeleteTexture(scene); GL.DeleteTexture(depthTexture); GL.DeleteTexture(output); GL.DeleteProgram(program);
Console.WriteLine($"SUCCESS {checks} assertions");
