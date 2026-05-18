using OpenTK.Mathematics;

namespace WydMapEditor;

/// <summary>
/// Câmera 3D orbital para o viewport do editor.
/// Controles: botão direito = orbit, meio = pan, scroll = zoom.
/// </summary>
public sealed class Camera3D
{
    // ── Parâmetros da câmera ────────────────────────────────────────────────
    public float   Yaw      = 225f;   // graus — rotação horizontal
    public float   Pitch    = 50f;    // graus — inclinação vertical  [10..85]
    public float   Distance = 130f;   // distância ao alvo
    public Vector3 Target   = new Vector3(64f, 0f, 64f);
    public float   Fov      = 45f;    // field of view em graus

    // ── Limites ─────────────────────────────────────────────────────────────
    private const float MinPitch    = 10f;
    private const float MaxPitch    = 85f;
    private const float MinDistance = 8f;
    private const float MaxDistance = 600f;

    // ── Matrizes ────────────────────────────────────────────────────────────
    public Vector3 EyePosition
    {
        get
        {
            float yr = MathHelper.DegreesToRadians(Yaw);
            float pr = MathHelper.DegreesToRadians(Pitch);
            return Target + new Vector3(
                Distance * MathF.Cos(pr) * MathF.Sin(yr),
                Distance * MathF.Sin(pr),
                Distance * MathF.Cos(pr) * MathF.Cos(yr));
        }
    }

    public Matrix4 ViewMatrix
        => Matrix4.LookAt(EyePosition, Target, Vector3.UnitY);

    public Matrix4 ProjectionMatrix(float aspect)
        => Matrix4.CreatePerspectiveFieldOfView(
            MathHelper.DegreesToRadians(Fov), aspect, 0.5f, 2000f);

    // ── Controles ───────────────────────────────────────────────────────────
    public void Orbit(float dYaw, float dPitch)
    {
        Yaw   = (Yaw + dYaw) % 360f;
        Pitch = Math.Clamp(Pitch + dPitch, MinPitch, MaxPitch);
    }

    public void Pan(float dx, float dy)
    {
        float yr    = MathHelper.DegreesToRadians(Yaw);
        var   right = new Vector3(MathF.Cos(yr), 0f, -MathF.Sin(yr));
        var   fwd   = new Vector3(MathF.Sin(yr), 0f,  MathF.Cos(yr));
        float scale = Distance * 0.0018f;
        Target += right * (-dx * scale) + fwd * (dy * scale);
        Target.X = Math.Clamp(Target.X, -20f, 148f);
        Target.Z = Math.Clamp(Target.Z, -20f, 148f);
    }

    public void Zoom(float delta)
        => Distance = Math.Clamp(Distance - delta * Distance * 0.12f, MinDistance, MaxDistance);

    public void Reset()
    {
        Target   = new Vector3(64f, 0f, 64f);
        Yaw      = 225f;
        Pitch    = 50f;
        Distance = 130f;
    }

    /// <summary>
    /// Projeta um ponto de tela (pixelX, pixelY) num raio no espaço 3D.
    /// Retorna false se a projeção falhar.
    /// </summary>
    public bool Unproject(float px, float py, float vpW, float vpH,
                          out Vector3 rayOrigin, out Vector3 rayDir)
    {
        var proj = ProjectionMatrix(vpW / vpH);
        var view = ViewMatrix;

        // NDC
        float nx = (2f * px / vpW) - 1f;
        float ny = 1f - (2f * py / vpH);

        // Convenção row-vector: clip = worldVec * (view * proj)
        // Inversa correta: worldVec = clipVec * Invert(view * proj)
        var invVP = Matrix4.Invert(view * proj);
        var near  = new Vector4(nx, ny, -1f, 1f) * invVP;
        var far   = new Vector4(nx, ny,  1f, 1f) * invVP;

        if (Math.Abs(near.W) < 1e-6f || Math.Abs(far.W) < 1e-6f)
        {
            rayOrigin = Vector3.Zero;
            rayDir    = Vector3.UnitZ;
            return false;
        }

        var n3 = new Vector3(near.X, near.Y, near.Z) / near.W;
        var f3 = new Vector3(far.X,  far.Y,  far.Z)  / far.W;
        rayOrigin = n3;
        rayDir    = (f3 - n3).Normalized();
        return true;
    }

    /// <summary>
    /// Intersecta o raio com o plano Y = planeY e devolve o tile (tx, ty).
    /// </summary>
    public static bool RayHitPlane(Vector3 origin, Vector3 dir, float planeY,
                                   out int tileX, out int tileY)
    {
        tileX = tileY = -1;
        if (Math.Abs(dir.Y) < 1e-6f) return false;
        float t = (planeY - origin.Y) / dir.Y;
        if (t < 0f) return false;
        var hit = origin + dir * t;
        int tx = (int)(hit.X / 2f);
        int ty = (int)(hit.Z / 2f);
        // Reject only if truly far outside the map (> 4 tiles buffer)
        if (tx < -4 || tx >= 68 || ty < -4 || ty >= 68) return false;
        // Clamp to valid tile range so cursor always snaps to map edge
        tileX = Math.Clamp(tx, 0, 63);
        tileY = Math.Clamp(ty, 0, 63);
        return true;
    }

    /// <summary>
    /// Intersecta o raio com o plano Y = planeY e devolve o ponto 3D exato de hit.
    /// </summary>
    public static bool RayHitPlaneWorld(Vector3 origin, Vector3 dir, float planeY, out Vector3 hitPoint)
    {
        hitPoint = Vector3.Zero;
        if (Math.Abs(dir.Y) < 1e-6f) return false;
        float t = (planeY - origin.Y) / dir.Y;
        if (t < 0f) return false;
        hitPoint = origin + dir * t;
        return true;
    }
}
