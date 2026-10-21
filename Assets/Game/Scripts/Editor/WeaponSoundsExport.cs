using System.IO;
using UnityEditor;
using UnityEngine;

// Tools > BeerAndDragon > Export Weapon Sounds: сохраняет сгенерированные звуки дробовика в WAV (build/sounds),
// чтобы послушать их без запуска игры или взять за основу для своих.
// Из командной строки: -executeMethod WeaponSoundsExport.Export
public static class WeaponSoundsExport
{
    [MenuItem("Tools/BeerAndDragon/Export Weapon Sounds")]
    public static void Export()
    {
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "build", "sounds");
        Directory.CreateDirectory(dir);
        for (int i = 0; i < 3; i++) Write(Path.Combine(dir, $"shot{i}.wav"), WeaponSounds.SynthShot(100 + i));
        Write(Path.Combine(dir, "reload.wav"), WeaponSounds.SynthReload());
        Write(Path.Combine(dir, "empty-click.wav"), ClipData(Weapon.CreateEmptyClick()));
        Debug.Log($"[Sounds] WAV сохранены в {dir}");
        if (!Application.isBatchMode) EditorUtility.RevealInFinder(dir);
    }

    private static float[] ClipData(AudioClip clip)
    {
        float[] d = new float[clip.samples];
        clip.GetData(d, 0);
        Object.DestroyImmediate(clip);
        return d;
    }

    // 16-бит PCM, моно
    private static void Write(string path, float[] data)
    {
        using (BinaryWriter w = new BinaryWriter(File.Create(path)))
        {
            int rate = WeaponSounds.SampleRate, bytes = data.Length * 2;
            w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes);
            w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(bytes);
            foreach (float s in data) w.Write((short)Mathf.RoundToInt(Mathf.Clamp(s, -1f, 1f) * 32767f));
        }
    }
}
