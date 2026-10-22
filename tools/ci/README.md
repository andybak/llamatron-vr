# Unity build licensing

1. `unity-personal.ulf` is the public Unity Personal license embedded in
   [Open Brush's build workflow](https://github.com/icosa-foundation/open-brush/blob/main/.github/workflows/build.yml).
   It was copied on 2026-10-22, along with the public test-account fallback used
   by that workflow. It is deliberately versioned public configuration.
2. The web workflow uses this fallback only when neither `UNITY_LICENSE` nor
   `UNITY_SERIAL` is configured. Custom credentials take precedence and are
   passed through GitHub's environment file without printing their values.
3. GameCI Unity Builder v5 matches Open Brush's builder version. Successful
   activation with Unity 6000.3.18f1 must be verified on a GitHub runner; Open
   Brush's successful builds use a different Editor version.
