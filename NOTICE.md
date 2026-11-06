# Licensing and third-party materials

Original source code, tools, technical documentation, and project-authored content in this repository are licensed under the root [MIT License](LICENSE). Third-party materials retain their own licenses and attribution; the root license does not replace them.

## Character assets

The character models and textures are generated from MakeHuman CC0 assets using Blender and MPFB2. See [the character asset notice](unity/WrestlingSimulator/Assets/Characters/LICENSE.md) and the [MakeHuman asset pack sources](https://static.makehumancommunity.org/assets/assetpacks/index.html). Generated renders containing these characters retain this source attribution.

## Harmony4D motion and camera references

Imported Harmony4D motion takes, camera reference images, and comparison materials derived from that dataset are attributed to Rawal Khirodkar, Jyun-Ting Song, Jinkun Cao, Zhengyi Luo, and Kris Kitani (Carnegie Mellon University), Harmony4D, NeurIPS 2024.

- Dataset: <https://huggingface.co/datasets/Jyun-Ting/Harmony4D> (declared MIT in its dataset card).
- Upstream project and license: <https://github.com/jyuntins/harmony4d>.
- Per-take source metadata: [`data/motion/takes/`](data/motion/takes/).
- Camera and comparison provenance: [`docs/images/realism-review/`](docs/images/realism-review/) and [`tools/motion/harmony4d/reviews/`](tools/motion/harmony4d/reviews/).

The upstream MIT notice is reproduced below:

> MIT License
>
> Copyright (c) 2024 Jyun-Ting Song
>
> Permission is hereby granted, free of charge, to any person obtaining a copy
> of this software and associated documentation files (the "Software"), to deal
> in the Software without restriction, including without limitation the rights
> to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
> copies of the Software, and to permit persons to whom the Software is
> furnished to do so, subject to the following conditions:
>
> The above copyright notice and this permission notice shall be included in all
> copies or substantial portions of the Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
> IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
> FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
> AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
> LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
> OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
> SOFTWARE.

## Dependencies and external research sources

Unity and third-party packages remain subject to their own licenses. The project's MIT license does not license the Unity editor or runtime itself.

Links to footage, papers, datasets, and commercial assets in [the research source notes](docs/research/footage-sources.md) are references, not a grant of redistribution rights. Follow the recorded license for each imported source; do not assume that a linked resource is covered by this repository's MIT license.
