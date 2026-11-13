<script setup lang="ts">
defineProps<{
  videoId: string
  title: string
}>()
</script>

<template>
  <figure class="youtube-video">
    <iframe
      :src="`https://www.youtube-nocookie.com/embed/${videoId}`"
      :title="title"
      width="560"
      height="315"
      loading="lazy"
      referrerpolicy="strict-origin-when-cross-origin"
      allow="encrypted-media; picture-in-picture; fullscreen"
      allowfullscreen
    />
    <figcaption>
      <a :href="`https://youtu.be/${videoId}`" target="_blank" rel="noopener noreferrer">
        {{ title }} — watch on YouTube
      </a>
    </figcaption>
  </figure>
</template>

<style scoped>
.youtube-video {
  margin: 24px 0;
}

.youtube-video iframe {
  display: block;
  width: 100%;
  height: auto;
  aspect-ratio: 16 / 9;
  border: 1px solid var(--vp-c-divider);
  border-radius: 14px;
  background: var(--vp-c-bg-soft);
}

.youtube-video figcaption {
  margin-top: 8px;
  font-size: 0.92rem;
  line-height: 1.5;
}
</style>
