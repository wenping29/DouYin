#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
gen-sites.py — 生成 50 站点评测集（广告请求样本）

为什么需要它：
  MV3 扩展的拦截率 baseline 一直没有定义（PRD 第十五节 RTM 评审 REV-07 遗留项）。
  本脚本产出 evaluation/sites.json：50 个主流站点 × 若干"典型广告请求样本"，
  供 run-eval.js 做静态覆盖率评测，也供后续 Puppeteer 真机评测复用。

样本是"构造的预期广告请求"（基于公开已知的广告网络域名），不代表真实抓包。
每个样本标注：广告网络域名、资源类型、第一/第三方、发起站点、期望结果（均应为 block）。

运行：python3 evaluation/gen-sites.py
"""
import json
import random

random.seed(20261104)

# 广告网络条目: (host, resourceType, party)
# 注意：使用 EasyList 实际在网络层拦截的「子域」（已核对源文件），避免用裸域名导致网络层评测失真。
# 其中 ad.qt.qq.com / ad.toutiao.com / mimg.126.net 在 EasyList 体系内零网络规则，
# 仅用于暴露「网络层零覆盖、需靠 cosmetic 或补充列表」的真实缺口。
GOOGLE = [
    ("ad.doubleclick.net", "image", "third"),
    ("pagead2.googlesyndication.com", "script", "third"),
    ("googletagservices.com", "script", "third"),
    ("googleadservices.com", "script", "third"),
    ("adservice.google.com", "image", "third"),
]
INTL = [
    ("adnxs.com", "image", "third"),
    ("adsrvr.org", "image", "third"),
    ("criteo.com", "script", "third"),
    ("taboola.com", "sub_frame", "third"),
    ("outbrain.com", "sub_frame", "third"),
    ("scorecardresearch.com", "image", "third"),
    ("pubmatic.com", "script", "third"),
    ("rubiconproject.com", "script", "third"),
]
# 国内广告网络（用于暴露第一方/国内网络缺口；部分在 EasyList 体系内网络层零覆盖）
CN = [
    ("pos.baidu.com", "image", "first"),
    ("cpro.baidu.com", "script", "first"),
    ("ad.qt.qq.com", "image", "first"),
    ("iad.gtimg.com", "image", "first"),
    ("ad.toutiao.com", "script", "third"),
    ("tanx.com", "script", "first"),
    ("m.alimama.com", "script", "first"),
    ("pos.360.cn", "image", "first"),
    ("sax.sina.com.cn", "image", "third"),
    ("mimg.126.net", "image", "third"),
]

# 站点: (name, url, category, ad_type_pref)
SITES = [
    # 新闻
    ("新浪新闻", "https://news.sina.com.cn", "新闻", "信息流"),
    ("搜狐新闻", "https://news.sohu.com", "新闻", "信息流"),
    ("网易新闻", "https://news.163.com", "新闻", "信息流"),
    ("腾讯新闻", "https://news.qq.com", "新闻", "信息流"),
    ("凤凰网", "https://www.ifeng.com", "新闻", "展示"),
    ("澎湃新闻", "https://www.thepaper.cn", "新闻", "信息流"),
    ("环球网", "https://www.huanqiu.com", "新闻", "展示"),
    ("人民网", "https://www.people.com.cn", "新闻", "展示"),
    ("新华网", "https://www.xinhuanet.com", "新闻", "展示"),
    ("财新网", "https://www.caixin.com", "新闻", "展示"),
    # 视频
    ("哔哩哔哩", "https://www.bilibili.com", "视频", "贴片"),
    ("优酷", "https://www.youku.com", "视频", "贴片"),
    ("爱奇艺", "https://www.iqiyi.com", "视频", "贴片"),
    ("腾讯视频", "https://v.qq.com", "视频", "贴片"),
    ("芒果TV", "https://www.mgtv.com", "视频", "贴片"),
    ("抖音", "https://www.douyin.com", "视频", "信息流"),
    ("快手", "https://www.kuaishou.com", "视频", "信息流"),
    # 社交
    ("微博", "https://weibo.com", "社交", "信息流"),
    ("微信", "https://weixin.qq.com", "社交", "信息流"),
    ("QQ", "https://im.qq.com", "社交", "展示"),
    ("知乎", "https://www.zhihu.com", "社交", "信息流"),
    ("豆瓣", "https://www.douban.com", "社交", "展示"),
    ("百度贴吧", "https://tieba.baidu.com", "社交", "信息流"),
    ("小红书", "https://www.xiaohongshu.com", "社交", "信息流"),
    # 电商
    ("淘宝", "https://www.taobao.com", "电商", "展示"),
    ("京东", "https://www.jd.com", "电商", "展示"),
    ("拼多多", "https://www.pinduoduo.com", "电商", "展示"),
    ("天猫", "https://www.tmall.com", "电商", "展示"),
    ("苏宁", "https://www.suning.com", "电商", "展示"),
    ("唯品会", "https://www.vip.com", "电商", "展示"),
    # 搜索/门户
    ("百度", "https://www.baidu.com", "搜索", "展示"),
    ("搜狗", "https://www.sogou.com", "搜索", "展示"),
    ("360搜索", "https://www.so.com", "搜索", "展示"),
    ("必应", "https://www.bing.com", "搜索", "展示"),
    ("hao123", "https://www.hao123.com", "门户", "展示"),
    ("腾讯网", "https://www.qq.com", "门户", "信息流"),
    # 论坛/社区/科技
    ("天涯", "https://www.tianya.cn", "论坛", "展示"),
    ("虎扑", "https://www.hupu.com", "论坛", "信息流"),
    ("V2EX", "https://www.v2ex.com", "论坛", "展示"),
    ("CSDN", "https://www.csdn.net", "科技", "展示"),
    ("掘金", "https://juejin.cn", "科技", "信息流"),
    ("简书", "https://www.jianshu.com", "科技", "展示"),
    ("少数派", "https://sspai.com", "科技", "展示"),
    ("IT之家", "https://www.ithome.com", "科技", "信息流"),
    ("什么值得买", "https://www.smzdm.com", "科技", "信息流"),
    # 金融/游戏/国际
    ("东方财富", "https://www.eastmoney.com", "金融", "展示"),
    ("同花顺", "https://www.10jqka.com.cn", "金融", "展示"),
    ("雪球", "https://xueqiu.com", "金融", "信息流"),
    ("17173", "https://www.17173.com", "游戏", "展示"),
    ("游民星空", "https://www.gamersky.com", "游戏", "展示"),
    ("TapTap", "https://www.taptap.cn", "游戏", "展示"),
    ("YouTube", "https://www.youtube.com", "国际", "贴片"),
    ("Facebook", "https://www.facebook.com", "国际", "信息流"),
    ("Reddit", "https://www.reddit.com", "国际", "信息流"),
    ("CNN", "https://www.cnn.com", "国际", "展示"),
    ("Wikipedia", "https://www.wikipedia.org", "国际", "展示"),
    ("Amazon", "https://www.amazon.com", "国际", "展示"),
]

RT_MAP = {"image": "image", "script": "script", "sub_frame": "sub_frame"}


def build_samples(name, url, ad_type):
    host = url.split("//", 1)[1].split("/", 1)[0]
    picks = [random.choice(GOOGLE), random.choice(INTL), random.choice(CN)]
    samples = []
    for i, (ad_host, rt, party) in enumerate(picks):
        ad_url = f"https://{ad_host}/delivery/{random.randint(1000,9999)}/ad?site={host}&slot={i}"
        samples.append({
            "url": ad_url,
            "type": ad_type,
            "resourceType": RT_MAP[rt],
            "party": party,
            "initiator": host,
            "expected": "block",  # 理想中这些广告请求都应收敛拦截
        })
    return samples


def main():
    sites = []
    for idx, (name, url, category, ad_type) in enumerate(SITES, start=1):
        sites.append({
            "id": f"s{idx:02d}",
            "name": name,
            "url": url,
            "category": category,
            "samples": build_samples(name, url, ad_type),
        })
    out = {
        "meta": {
            "generatedAt": "2026-11-04",
            "description": "50 站点广告请求评测集（构造样本，非真实抓包）",
            "totalSites": len(sites),
            "totalSamples": sum(len(s["samples"]) for s in sites),
            "coverageMetric": "规则命中 block 的样本数 / 总样本数（静态，不含 cosmetic 层）",
            "note": "expected 均为 block，代表理想拦截；未被规则命中的即漏拦(leak)。",
        },
        "sites": sites,
    }
    with open("evaluation/sites.json", "w", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, indent=2)
    print(f"[OK] 生成 {len(sites)} 站点 / "
          f"{out['meta']['totalSamples']} 样本 -> evaluation/sites.json")


if __name__ == "__main__":
    main()
