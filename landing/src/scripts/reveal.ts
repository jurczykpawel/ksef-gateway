import { animate, inView } from "motion";

interface RevealOptions {
  duration?: number;
  delayStep?: number;
  margin?: string;
  distance?: number;
}

const reducedMotion = () =>
  window.matchMedia("(prefers-reduced-motion: reduce)").matches;

/**
 * Scroll-reveal animation shared by every section. Motion One runs on WAAPI,
 * which a `prefers-reduced-motion` CSS media query cannot stop - so the check
 * lives here: with reduced motion the elements are never hidden in the first
 * place and no animation runs (WCAG 2.3.3).
 *
 * After the animation finishes the inline opacity/transform are cleared -
 * Motion One leaves them behind, and an inline `transform` silently kills
 * every CSS :hover transform (e.g. .card-lift) on the same element.
 */
export function reveal(selector: string, opts: RevealOptions = {}) {
  if (reducedMotion()) return;

  const {
    duration = 0.5,
    delayStep = 0.06,
    margin = "0px 0px -30px 0px",
    distance = 14,
  } = opts;

  document.querySelectorAll<HTMLElement>(selector).forEach((el, i) => {
    el.style.opacity = "0";
    el.style.transform = `translateY(${distance}px)`;
    inView(
      el,
      () => {
        animate(
          el,
          { opacity: 1, transform: "translateY(0px)" },
          { duration, delay: i * delayStep, easing: [0.22, 1, 0.36, 1] },
        ).finished.then(() => {
          el.style.opacity = "";
          el.style.transform = "";
        });
      },
      { margin },
    );
  });
}

/**
 * Cascade: when the container scrolls into view, its matching children pop in
 * one after another (used for checklist icons/rows). Same reduced-motion and
 * inline-style-cleanup rules as reveal().
 */
export function cascade(
  containerSelector: string,
  childSelector: string,
  opts: { stagger?: number; distance?: number; startDelay?: number } = {},
) {
  if (reducedMotion()) return;

  const { stagger = 0.09, distance = 10, startDelay = 0.15 } = opts;

  document.querySelectorAll<HTMLElement>(containerSelector).forEach((container) => {
    const items = container.querySelectorAll<HTMLElement>(childSelector);
    items.forEach((item) => {
      item.style.opacity = "0";
      item.style.transform = `translateX(-${distance}px)`;
    });
    inView(
      container,
      () => {
        items.forEach((item, i) => {
          animate(
            item,
            { opacity: 1, transform: "translateX(0px)" },
            { duration: 0.4, delay: startDelay + i * stagger, easing: [0.22, 1, 0.36, 1] },
          ).finished.then(() => {
            item.style.opacity = "";
            item.style.transform = "";
          });
        });
      },
      { margin: "0px 0px -60px 0px" },
    );
  });
}
