# KageAnime — плагин для Flow Launcher

Поиск аниме прямо из Flow Launcher с открытием плеера на [anime.hitler.site](https://anime.hitler.site).

## Возможности

- `ka <название>` — поиск по русским и английским названиям (Kodik + Yummy + AniLibria)
  - постер, год, рейтинг, тип и источник в подсказке
  - **Enter открывает тайтл сразу в плеере** (deep-link webapp)
  - Shift+Enter — контекстное меню: страница поиска на сайте / прямая ссылка
- `ka ongoing` — последние онгоинги
- `ka random` — случайное аниме

## Установка

Из Plugin Store Flow Launcher: `pm install KageAnime`

Вручную: скачайте zip со [страницы релизов](../../releases) и распакуйте в
`%APPDATA%\FlowLauncher\Plugins\KageAnime`, затем перезапустите Flow.

## Технически

- Только стандартная библиотека Python (никаких pip-зависимостей)
- Кеш поиска 5 минут, постеры кешируются локально
- Fuzzy-поиск с транслитерацией RU↔EN на стороне API

Лицензия: MIT
