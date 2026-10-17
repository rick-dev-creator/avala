import "./avala-c4.js";

const diagrams = /^\s*C4(Context|Container|Component|Dynamic)\b/;

export default {
  start: () => {
    document.querySelectorAll("article pre code.lang-mermaid").forEach(code => {
      const source = code.textContent;
      if (!diagrams.test(source)) {
        return;
      }
      const player = document.createElement("avala-c4");
      player.setAttribute("source", source);
      code.parentElement.replaceWith(player);
    });
  }
};
