#!/bin/sh
# Все небеса с созвездиями 28 стоянок. Запуск из корня репозитория: sh ArtSource/Sky/make_all_skies.sh
# Нужен uv (https://docs.astral.sh/uv/) — numpy и opencv он подтянет сам во временное окружение.
#
# Космос после босса — остаются сектора ещё живых боссов (порядок: весна → лето → осень → зима):
#   после Цинлуна — юг, запад, север; после Чжуцюэ — запад, север; после Байху — север; после Сюаньу — пустота.
# Уровень босса — только его сектор, в зените.
set -e
cd "$(dirname "$0")"
OUT=../../Assets/_Game/Art/Space
run() { uv run --no-project --with numpy --with opencv-python-headless python -I make_space_sky.py "$@"; }

run "$OUT/Sky_Space_AfterQinglong.hdr" --sectors south,west,north
run "$OUT/Sky_Space_AfterZhuque.hdr"   --sectors west,north
run "$OUT/Sky_Space_AfterBaihu.hdr"    --sectors north
run "$OUT/Sky_Space_Nebula.hdr"                                   # после Сюаньу — пустота

run "$OUT/Sky_Boss_Qinglong.hdr" --zenith east
run "$OUT/Sky_Boss_Zhuque.hdr"   --zenith south
run "$OUT/Sky_Boss_Baihu.hdr"    --zenith west
run "$OUT/Sky_Boss_Xuanwu.hdr"   --zenith north
