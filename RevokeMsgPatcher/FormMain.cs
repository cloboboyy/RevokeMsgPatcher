﻿using RevokeMsgPatcher.Model;
using RevokeMsgPatcher.Modifier;
using RevokeMsgPatcher.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace RevokeMsgPatcher
{
    public partial class FormMain : Form
    {
        // 当前使用的修改者
        private AppModifier modifier = null;

        private WechatModifier wechatModifier = null;
        private QQModifier qqModifier = null;
        private TIMModifier timModifier = null;

        private string thisVersion;
        private bool needUpdate = false;

        private GAHelper ga = new GAHelper(); // Google Analytics 记录

        public void InitModifier()
        {
            // 从配置文件中读取配置
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            Bag bag = serializer.Deserialize<Bag>(Properties.Resources.PatchJson);

            // 初始化每个应用对应的修改者
            wechatModifier = new WechatModifier(bag.Apps["Wechat"]);
            qqModifier = new QQModifier(bag.Apps["QQ"]);
            timModifier = new TIMModifier(bag.Apps["TIM"]);

            rbtWechat.Tag = wechatModifier;
            rbtQQ.Tag = qqModifier;
            rbtTIM.Tag = timModifier;

            // 默认微信
            rbtWechat.Enabled = true;
            modifier = wechatModifier;
        }

        public FormMain()
        {
            InitializeComponent();

            // 标题加上版本号
            string currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
            if (currentVersion.Length > 3)
            {
                thisVersion = currentVersion.Substring(0, 3);
                currentVersion = " v" + thisVersion;
            }
            this.Text += currentVersion;

            InitModifier();
            InitControls();

            ga.RequestPageView("/main", "进入主界面");
        }

        private void InitControls()
        {
            // 自动获取应用安装路径
            txtPath.Text = modifier.FindInstallPath();
            lblVersion.Text = modifier.GetVersion();
            // 显示是否能够备份还原
            if (!string.IsNullOrEmpty(txtPath.Text))
            {
                modifier.InitEditors(txtPath.Text);
                btnRestore.Enabled = modifier.BackupExists();
            }
        }

        private void btnPatch_Click(object sender, EventArgs e)
        {
            if (!modifier.IsAllFilesExist(txtPath.Text))
            {
                MessageBox.Show("请选择正确的安装路径!");
                return;
            }

            // 记录点了什么应用的防撤回
            ga.RequestPageView(GetCheckedRadioButtonNameEn() + "/patch", "点击防撤回");

            EnableAllButton(false);
            // a.重新初始化编辑器
            modifier.InitEditors(txtPath.Text);
            // b.计算SHA1，验证文件完整性，寻找对应的补丁信息
            try
            {
                modifier.ValidateAndFindModifyInfo();
            }
            catch (Exception ex)
            {
                ga.RequestPageView(GetCheckedRadioButtonNameEn() + "/patch/sha1/ex", ex.Message);
                MessageBox.Show(ex.Message);
                EnableAllButton(true);
                btnRestore.Enabled = modifier.BackupExists();
                return;
            }
            // c.打补丁
            try
            {
                modifier.Patch();
                ga.RequestPageView(GetCheckedRadioButtonNameEn() + "/patch/succ", "防撤回成功");
                MessageBox.Show("补丁安装成功！");
                EnableAllButton(true);
                btnRestore.Enabled = modifier.BackupExists();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                ga.RequestPageView(GetCheckedRadioButtonNameEn() + "/patch/ex", ex.Message);
                MessageBox.Show(ex.Message + " 请以管理员权限启动本程序，并确认微信处于关闭状态。");
                EnableAllButton(true);
                btnRestore.Enabled = modifier.BackupExists();
            }

        }

        private void txtPath_TextChanged(object sender, EventArgs e)
        {
            if (modifier.IsAllFilesExist(txtPath.Text))
            {
                modifier.InitEditors(txtPath.Text);
                btnRestore.Enabled = modifier.BackupExists();
            }
            else
            {
                btnPatch.Enabled = false;
                btnRestore.Enabled = false;
            }
        }

        private void btnChoosePath_Click(object sender, EventArgs e)
        {
            FolderBrowserDialog dialog = new FolderBrowserDialog();
            dialog.Description = "请选择安装路径";
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                if (string.IsNullOrEmpty(dialog.SelectedPath) || !modifier.IsAllFilesExist(dialog.SelectedPath))
                {
                    MessageBox.Show("无法找到此应用的关键文件，请选择正确的安装路径!");
                }
                else
                {
                    txtPath.Text = dialog.SelectedPath;
                }
            }
        }

        private void btnRestore_Click(object sender, EventArgs e)
        {
            EnableAllButton(false);
            try
            {
                modifier.Restore();
                MessageBox.Show("还原成功！");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                MessageBox.Show(ex.Message);
            }
            EnableAllButton(true);
            btnRestore.Enabled = modifier.BackupExists();
        }

        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            System.Diagnostics.Process.Start("https://github.com/huiyadanli/RevokeMsgPatcher");
        }

        private async void FormMain_Load(object sender, EventArgs e)
        {
            // 异步获取最新的补丁信息
            string json = await GetPathJsonAsync();
            if (string.IsNullOrEmpty(json))
            {
                lblUpdatePachJson.Text = "[ 获取失败 ]";

            }
            else
            {
                try
                {
                    JavaScriptSerializer serializer = new JavaScriptSerializer();
                    Bag bag = serializer.Deserialize<Bag>(json);

                    wechatModifier.Config = bag.Apps["Wechat"];
                    qqModifier.Config = bag.Apps["QQ"];
                    timModifier.Config = bag.Apps["TIM"];

                    if (Convert.ToDecimal(bag.LatestVersion) > Convert.ToDecimal(thisVersion))
                    {
                        needUpdate = true;
                        lblUpdatePachJson.Text = $"[ 请到软件主页下载最新版本 {bag.LatestVersion} ]";
                    }
                    else
                    {
                        needUpdate = false;
                        lblUpdatePachJson.Text = "[ 获取成功 ]";
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    lblUpdatePachJson.Text = "[ 更换新配置时异常 ]";
                }
            }
        }

        private async Task<string> GetPathJsonAsync()
        {
            string downStr = null;
            try
            {
                downStr = await HttpUtil.Client.GetStringAsync("https://huiyadanli.coding.me/i/revokemsg/05.json");
            }
            catch (Exception ex1)
            {
                Console.WriteLine(ex1.Message);
                try
                {
                    downStr = await HttpUtil.Client.GetStringAsync("https://www.huiyadan.com/i/revokemsg/05.json");
                }
                catch (Exception ex2)
                {
                    Console.WriteLine(ex2.Message);
                }
            }
            return downStr;
        }

        private void lblUpdatePachJson_Click(object sender, EventArgs e)
        {
            string tips = "";
            if (needUpdate)
            {
                tips += "【当前存在最新版本，点击确定进入软件主页下载最新版本。】" + Environment.NewLine + Environment.NewLine;
            }
            tips += "支持以下版本" + Environment.NewLine;
            tips += " ➯ 微信：" + wechatModifier.Config.GetSupportVersionStr() + Environment.NewLine;
            tips += " ➯ QQ：" + qqModifier.Config.GetSupportVersionStr() + Environment.NewLine;
            tips += " ➯ TIM：" + timModifier.Config.GetSupportVersionStr() + Environment.NewLine;

            DialogResult dr = MessageBox.Show(tips, "当前支持防撤回的版本", MessageBoxButtons.OKCancel);
            if (dr == DialogResult.OK && needUpdate)
            {
                System.Diagnostics.Process.Start("https://github.com/huiyadanli/RevokeMsgPatcher/releases");
            }
        }

        private void radioButtons_CheckedChanged(object sender, EventArgs e)
        {
            ga.RequestPageView(GetCheckedRadioButtonNameEn() + "/switch", "切换标签页");
            EnableAllButton(false);
            RadioButton radioButton = sender as RadioButton;
            // 切换使用不同的防撤回对象
            if (rbtWechat.Checked)
            {
                modifier = (WechatModifier)rbtWechat.Tag;
            }
            else if (rbtQQ.Checked)
            {
                modifier = (QQModifier)rbtQQ.Tag;
            }
            else if (rbtTIM.Checked)
            {
                modifier = (TIMModifier)rbtTIM.Tag;
            }
            txtPath.Text = modifier.FindInstallPath();
            lblVersion.Text = modifier.GetVersion();
            EnableAllButton(true);
            // 显示是否能够备份还原
            if (!string.IsNullOrEmpty(txtPath.Text))
            {
                modifier.InitEditors(txtPath.Text);
                btnRestore.Enabled = modifier.BackupExists();
            }
        }

        private string GetCheckedRadioButtonNameEn()
        {
            if (rbtWechat.Checked)
            {
                return "wechat";
            }
            else if (rbtQQ.Checked)
            {
                return "qq";
            }
            else if (rbtTIM.Checked)
            {
                return "tim";
            }
            return "none";
        }

        private void EnableAllButton(bool state)
        {
            foreach (Control c in this.Controls)
            {
                if (c is Button)
                {
                    c.Enabled = state;
                }
            }
        }
    }
}
