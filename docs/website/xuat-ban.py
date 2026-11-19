"""
Tạo nội dung phát hành (trang WordPress + PDF hướng dẫn) từ các file *.src.html.
Cách dùng (từ thư mục gốc dự án):
    python docs/website/xuat-ban.py [--download URL] [--img-base URL]
Kết quả: docs/website/xuat-ban/
"""
import argparse, datetime, hashlib, os, re, subprocess, sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
WEB = os.path.join(ROOT, 'docs', 'website')
OUT = os.path.join(WEB, 'xuat-ban')
SITE = 'https://caominhcomputer.com'

ap = argparse.ArgumentParser()
ap.add_argument('--installer', default=os.path.join(ROOT, 'artifacts', 'installer', 'ConNgoan-Setup-x64.exe'))
ap.add_argument('--download', default='https://github.com/cameraanninhso1-jpg/con-ngoan/releases/latest/download/ConNgoan-Setup-x64.exe')
ap.add_argument('--img-base', default=f'{SITE}/wp-content/uploads/2026/11/')
ap.add_argument('--guide-pdf', default=f'{SITE}/wp-content/uploads/2026/11/Huong-dan-cai-dat-va-su-dung-Con-Ngoan.pdf')
args = ap.parse_args()

props = open(os.path.join(ROOT, 'Directory.Build.props'), encoding='utf-8-sig').read()
version = re.search(r'<Version>([^<]+)</Version>', props).group(1)
data = open(args.installer, 'rb').read()
values = {
    'VERSION': version,
    'DATE': datetime.date.today().strftime('%d/%m/%Y'),
    'SIZE_MB': f'{len(data) / 1024 / 1024:.0f}',
    'SHA256': hashlib.sha256(data).hexdigest(),
    'DOWNLOAD': args.download,
    'GUIDE_PDF': args.guide_pdf,
    'PRIVACY_URL': f'{SITE}/con-ngoan-chinh-sach-rieng-tu/',
    'TERMS_URL': f'{SITE}/con-ngoan-dieu-khoan-su-dung/',
}

def fill(text, img):
    text = text.replace('{{IMG}}', img)
    for k, v in values.items():
        text = text.replace('{{' + k + '}}', v)
    left = re.findall(r'\{\{\w+\}\}', text)
    if left: sys.exit(f'Còn chỗ chưa điền: {set(left)}')
    return text

os.makedirs(OUT, exist_ok=True)
for name in ('con-ngoan', 'chinh-sach-rieng-tu', 'dieu-khoan-su-dung'):
    src = open(os.path.join(WEB, name + '.src.html'), encoding='utf-8').read()
    open(os.path.join(OUT, name + '.html'), 'w', encoding='utf-8').write(fill(src, args.img_base))

# PDF hướng dẫn: dùng ảnh cục bộ, in bằng Microsoft Edge (headless).
guide = fill(open(os.path.join(WEB, 'huong-dan-su-dung.src.html'), encoding='utf-8').read(),
             'file:///' + os.path.join(WEB, 'anh').replace('\\', '/') + '/')
html_path = os.path.join(OUT, 'huong-dan-su-dung.html')
open(html_path, 'w', encoding='utf-8').write(guide)
pdf_path = os.path.join(OUT, 'Huong-dan-su-dung-Con-Ngoan.pdf')
edge = r'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
subprocess.run([edge, '--headless=new', '--disable-gpu', '--no-pdf-header-footer', '--allow-file-access-from-files',
                f'--print-to-pdf={pdf_path}', 'file:///' + html_path.replace('\\', '/')], check=True, timeout=120)
print('Phiên bản', version, '| SHA-256', values['SHA256'])
print('Đã tạo:', ', '.join(sorted(os.listdir(OUT))))
