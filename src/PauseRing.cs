using System.Collections.Generic;
using SecretHistories.Entities;
using SecretHistories.UI;
using UnityEngine;
using UnityEngine.UI;

namespace MarginaliaMod
{
    // A pulsing ring around whatever the game just paused for, until it's unpaused. It's
    // parented to the token, so it follows it as the table pans and zooms.
    internal class PauseRing : MonoBehaviour
    {
        private const int TextureSize = 128;
        private static readonly Color Gold = new Color(1f, 0.82f, 0.4f, 1f);

        private static readonly List<PauseRing> Rings = new List<PauseRing>();
        private static Sprite _sprite;

        private Image _image;
        private float _born;

        public static void Clear()
        {
            foreach (var ring in Rings)
            {
                if (ring != null)
                    Destroy(ring.gameObject);
            }
            Rings.Clear();
        }

        public static void Show(Token token)
        {
            token = Visible(token);
            if (token == null || token.Defunct)
                return;
            var parent = token.TokenRectTransform != null ? token.TokenRectTransform : (RectTransform)token.transform;

            var go = new GameObject("MarginaliaPauseRing", typeof(RectTransform));
            go.layer = token.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.SetAsLastSibling();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            float size = Mathf.Max(TokenSize(token, parent).x, TokenSize(token, parent).y) * 1.5f;
            rect.sizeDelta = new Vector2(size, size);
            go.AddComponent<LayoutElement>().ignoreLayout = true;

            var ring = go.AddComponent<PauseRing>();
            ring._image = go.AddComponent<Image>();
            ring._image.sprite = RingSprite();
            ring._image.raycastTarget = false;
            ring._image.color = Gold;
            ring._born = Time.unscaledTime;
            Rings.Add(ring);
        }

        // A card in a closed verb window can't be seen, so ring the verb instead.
        private static Token Visible(Token token)
        {
            for (int depth = 0; depth < 8 && token != null; depth++)
            {
                if (!(token.Sphere?.GetContainer() is Situation situation) || situation.IsOpen)
                    break;
                token = situation.Token;
            }
            return token;
        }

        private static Vector2 TokenSize(Token token, RectTransform fallback)
        {
            try
            {
                var size = new Vector2(token.GetManifestationWidth(), token.GetManifestationHeight());
                if (size.x > 0f && size.y > 0f)
                    return size;
            }
            catch
            {
                // No manifestation yet; use the token's own rect.
            }
            return fallback.rect.size;
        }

        private void Update()
        {
            var heart = Watchman.Exists<Heart>() ? Watchman.Get<Heart>() : null;
            if (heart == null || !heart.IsPaused())
            {
                Rings.Remove(this);
                Destroy(gameObject);
                return;
            }
            // Closes in from three times the size, then keeps pulsing.
            float age = Time.unscaledTime - _born;
            float arrive = Mathf.Clamp01(age / 0.35f);
            float scale = Mathf.Lerp(3f, 1f, 1f - (1f - arrive) * (1f - arrive)) + 0.08f * Mathf.Sin(age * 7f);
            transform.localScale = new Vector3(scale, scale, 1f);
            var color = Gold;
            color.a = 0.65f + 0.35f * Mathf.Sin(age * 7f);
            _image.color = color;
        }

        private static Sprite RingSprite()
        {
            if (_sprite != null)
                return _sprite;
            var texture = new Texture2D(TextureSize, TextureSize, TextureFormat.ARGB32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[TextureSize * TextureSize];
            float centre = (TextureSize - 1) / 2f, outer = TextureSize / 2f - 2f, inner = outer - 7f;
            for (int y = 0; y < TextureSize; y++)
            {
                for (int x = 0; x < TextureSize; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(centre, centre));
                    // One pixel of antialiasing on each edge.
                    float alpha = Mathf.Clamp01(Mathf.Min(outer - d, d - inner) + 0.5f);
                    pixels[y * TextureSize + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            _sprite = Sprite.Create(texture, new Rect(0f, 0f, TextureSize, TextureSize), new Vector2(0.5f, 0.5f));
            return _sprite;
        }
    }
}
