import { ref, watch, onUnmounted, type Ref } from 'vue';

/**
 * Returns a ref that becomes true when the target element scrolls into view.
 * Uses IntersectionObserver with a configurable rootMargin to trigger slightly before visible.
 * Handles elements that are conditionally rendered (v-if) by watching the template ref.
 */
export function useLazyLoad(
  target: Ref<HTMLElement | null>,
  rootMargin = '200px'
): Ref<boolean> {
  const isVisible = ref(false);
  let observer: IntersectionObserver | null = null;

  const observe = (el: HTMLElement | null) => {
    observer?.disconnect();
    if (!el || isVisible.value) return;
    observer = new IntersectionObserver(
      ([entry]) => {
        if (entry.isIntersecting) {
          isVisible.value = true;
          observer?.disconnect();
        }
      },
      { rootMargin }
    );
    observer.observe(el);
  };

  watch(target, observe, { immediate: true });

  onUnmounted(() => {
    observer?.disconnect();
  });

  return isVisible;
}
