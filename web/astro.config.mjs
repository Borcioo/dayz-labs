import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';

// NOTE: confirm owner/repo before first deploy. If the repo isn't "dzl",
// change `base` to `/<repo>` to match the GitHub Pages project path.
export default defineConfig({
  site: 'https://Borcioo.github.io',
  base: '/dzl',
  integrations: [
    starlight({
      title: 'dzl',
      description: 'A DayZ mod-development launcher for Windows — one core behind a CLI, an MCP server, and a tray app.',
      social: [
        { icon: 'github', label: 'GitHub', href: 'https://github.com/Borcioo/dzl' },
      ],
      customCss: ['./src/styles/theme.css'],
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
