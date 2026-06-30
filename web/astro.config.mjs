import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';

const ogImage = 'https://borcioo.github.io/dayz-labs/og.png';

// NOTE: confirm owner/repo before first deploy. If the repo isn't "dayz-labs",
// change `base` to `/<repo>` to match the GitHub Pages project path.
export default defineConfig({
  site: 'https://Borcioo.github.io',
  base: '/dayz-labs',
  integrations: [
    starlight({
      title: 'DayZ Labs',
      description: 'Free Windows launcher for DayZ mod development — dev server and client lifecycle, ordered mods, log diagnosis, a validating build and signing pipeline, and Central Economy editors.',
      social: [
        { icon: 'github', label: 'GitHub', href: 'https://github.com/Borcioo/dayz-labs' },
      ],
      customCss: ['./src/styles/theme.css'],
      components: {
        Footer: './src/components/Footer.astro',
      },
      head: [
        { tag: 'meta', attrs: { property: 'og:image', content: ogImage } },
        { tag: 'meta', attrs: { property: 'og:image:width', content: '1200' } },
        { tag: 'meta', attrs: { property: 'og:image:height', content: '630' } },
        { tag: 'meta', attrs: { name: 'twitter:image', content: ogImage } },
        {
          tag: 'script',
          attrs: { type: 'application/ld+json' },
          content: JSON.stringify({
            '@context': 'https://schema.org',
            '@type': 'SoftwareApplication',
            name: 'DayZ Labs',
            alternateName: 'dzl',
            applicationCategory: 'DeveloperApplication',
            operatingSystem: 'Windows 10, Windows 11',
            offers: { '@type': 'Offer', price: '0', priceCurrency: 'USD' },
            url: 'https://borcioo.github.io/dayz-labs/',
            downloadUrl: 'https://github.com/Borcioo/dayz-labs/releases/latest',
            description:
              'Free Windows launcher for DayZ mod development: dev server and client lifecycle, mods and presets, log diagnosis, a validating build and signing pipeline, and Central Economy editors.',
            license: 'https://www.gnu.org/licenses/gpl-3.0.html',
            author: { '@type': 'Person', name: 'Borcioo' },
          }),
        },
      ],
      sidebar: [
        {
          label: 'Overview',
          items: [
            { label: 'What is dzl', slug: 'overview/what-is-dzl' },
            { label: "Who it's for", slug: 'overview/who-its-for' },
            { label: 'The big idea', slug: 'overview/the-big-idea' },
            { label: 'Codebase map', slug: 'overview/codebase-map' },
          ],
        },
        { label: 'Features', autogenerate: { directory: 'features' } },
        { label: 'Guides', autogenerate: { directory: 'guides' } },
      ],
    }),
  ],
});
