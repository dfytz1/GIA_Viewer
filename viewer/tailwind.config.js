/** @type {import('tailwindcss').Config} */
export default {
  content: ["./index.html", "./src/**/*.{js,html}"],
  theme: {
    extend: {
      colors: {
        gia: {
          void: "#0a0b0d",
          panel: "rgba(18, 20, 24, 0.82)",
          border: "rgba(255,255,255,0.08)",
          accent: "#5b8cff",
          muted: "#9aa3b2",
        },
      },
      fontFamily: {
        sans: [
          "ui-sans-serif",
          "system-ui",
          "Segoe UI",
          "Inter",
          "Helvetica Neue",
          "Arial",
          "sans-serif",
        ],
      },
    },
  },
  plugins: [],
};
