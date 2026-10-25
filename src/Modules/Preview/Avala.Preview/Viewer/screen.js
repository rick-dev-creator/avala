const query = new URLSearchParams(location.search);
const wide = Number(query.get('w'));
const tall = Number(query.get('h'));
const page = query.get('page') ?? '';
const view = document.getElementById('page');

function fit() {
  const scale = Math.min(innerWidth / wide, innerHeight / tall);
  const left = (innerWidth - wide * scale) / 2;
  const top = (innerHeight - tall * scale) / 2;
  view.style.transform = `translate(${left}px, ${top}px) scale(${scale})`;
}

if (wide > 0 && tall > 0 && page.length > 0) {
  view.style.width = `${wide}px`;
  view.style.height = `${tall}px`;
  view.src = page;
  addEventListener('resize', fit);
  fit();
}
