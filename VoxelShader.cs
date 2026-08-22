namespace SilkWebGpuPbr;

public static class VoxelShader
{
    public const string Source = @"
struct SceneData {
    viewProjection: mat4x4<f32>,
    cameraPosition: vec3<f32>,
    lightDirection: vec3<f32>,
    lightColor: vec3<f32>,
    lightIntensity: f32,
    ambientColor: vec3<f32>,
    ambientIntensity: f32
}

@group(0) @binding(0) var<uniform> scene: SceneData;

struct VertexInput {
    @location(0) position: vec3<f32>,
    @location(1) normal: vec3<f32>,
    @location(2) color: vec4<f32>
}

struct VertexOutput {
    @builtin(position) position: vec4<f32>,
    @location(0) worldPosition: vec3<f32>,
    @location(1) normal: vec3<f32>,
    @location(2) color: vec4<f32>
}

@vertex
fn vs_main(input: VertexInput) -> VertexOutput {
    var output: VertexOutput;

    let worldPos = vec4<f32>(input.position, 1.0);
    output.position = scene.viewProjection * worldPos;
    output.worldPosition = worldPos.xyz;
    output.normal = input.normal;
    output.color = input.color;

    return output;
}

@fragment
fn fs_main(input: VertexOutput) -> @location(0) vec4<f32> {
    let normal = normalize(input.normal);

    // Directional lighting
    let nDotL = max(dot(normal, -scene.lightDirection), 0.0);
    let diffuse = scene.lightColor * scene.lightIntensity * nDotL;
    let ambient = scene.ambientColor * scene.ambientIntensity;

    let finalColor = input.color.rgb * (diffuse + ambient);

    // Gamma correction
    let gammaColor = pow(finalColor, vec3<f32>(1.0 / 2.2));

    // Distance fog blending
    let dist = distance(input.worldPosition, scene.cameraPosition);
    let fogStart = 60.0;
    let fogEnd = 200.0;
    let fogFactor = clamp((dist - fogStart) / (fogEnd - fogStart), 0.0, 1.0);
    let skyColor = vec3<f32>(0.4, 0.6, 0.9);
    let colorWithFog = mix(gammaColor, skyColor, fogFactor);

    return vec4<f32>(colorWithFog, input.color.a);
}
";
}
