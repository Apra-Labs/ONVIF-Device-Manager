# collect files after build of odm.ui.app and add following additinal folders:
mkdir build
copy C:\akhil\git\ONVIF-Device-Manager\odm\odm.ui.app\bin\Release\x64\*.* build\
copy libs\ffmpeg-git-a5c1a0c\x64\bin\*.dll build\

mkdir build\images
copy odm\odm.ui.views\images\wheel_zoom.cur build\images

mkdir build\locales
copy odm\odm.localization\locales\*.* build\locales

mkdir build\meta
copy odm\odm.ui.app\meta\*.* build\meta

mkdir build\logs
copy odm\odm.ui.app\logs\*.* build\logs
