using System.Collections.Generic;
using UnityEngine;

// 아이콘 불러오기: Assets/Resources/UI/Icons/<이름>.png (스킬 아이콘은 SkillId 이름과 같게)
// 같은 이름의 PNG로 바꿔 끼우면 그대로 반영됨. 임포트 설정이 Sprite가 아니어도 되도록 텍스처로 읽어 스프라이트를 만듦
public static class UiIcons
{
    const string Folder = "UI/Icons/";
    static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnPlay() => cache.Clear();

    public static Sprite Get(SkillId id) => Get(id.ToString());

    public static Sprite Get(string name)
    {
        if (cache.TryGetValue(name, out Sprite sprite)) return sprite;

        var texture = Resources.Load<Texture2D>(Folder + name);
        if (texture == null)
        {
            cache[name] = null;
            return null;
        }
        texture.wrapMode = TextureWrapMode.Clamp;
        sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
        sprite.name = name;
        cache[name] = sprite;
        return sprite;
    }
}
