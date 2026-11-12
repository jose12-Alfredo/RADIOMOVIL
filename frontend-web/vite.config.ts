import { readFileSync } from 'node:fs';
import { Agent } from 'node:https';
import { defineConfig, loadEnv } from 'vite';
import plugin from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';

// https://vitejs.dev/config/
export default defineConfig(({ command, mode }) => {
    if (command === 'build') return { plugins: [plugin(), tailwindcss()] };
    const env = loadEnv(mode, process.cwd(), '');
    if (!env.FRONTEND_HTTPS_CERT || !env.FRONTEND_HTTPS_KEY) {
        throw new Error('Configura FRONTEND_HTTPS_CERT y FRONTEND_HTTPS_KEY en .env.local. Consulta TECHNICAL.md.');
    }
    const target = env.TESTING_API_URL;
    if (target && (env.API_ENVIRONMENT !== 'Testing' || new URL(target).protocol !== 'https:' ||
        !['localhost', '127.0.0.1', '[::1]'].includes(new URL(target).hostname))) {
        throw new Error('El proxy requiere API_ENVIRONMENT=Testing y TESTING_API_URL HTTPS local.');
    }
    return {
        plugins: [plugin(), tailwindcss()],
        server: {
            host: 'localhost', port: 59257, strictPort: true,
            https: { cert: readFileSync(env.FRONTEND_HTTPS_CERT), key: readFileSync(env.FRONTEND_HTTPS_KEY) },
            proxy: target ? {
                '/api': {
                    target, secure: true, changeOrigin: true,
                    ...(env.TESTING_API_CA ? { agent: new Agent({ ca: readFileSync(env.TESTING_API_CA) }) } : {}),
                },
            } : undefined,
        },
    };
});
