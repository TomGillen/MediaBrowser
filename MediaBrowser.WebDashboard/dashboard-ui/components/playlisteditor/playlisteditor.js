﻿define([], function () {

    var lastPlaylistId = '';

    function redirectToPlaylist(id) {

        var context = getParameterByName('context');

        ApiClient.getItem(Dashboard.getCurrentUserId(), id).done(function (item) {

            Dashboard.navigate(LibraryBrowser.getHref(item, context));

        });
    }

    function onAddToPlaylistFormSubmit() {

        Dashboard.showLoadingMsg();

        var panel = $(this).parents('paper-dialog')[0];

        var playlistId = $('#selectPlaylistToAddTo', panel).val();

        if (playlistId) {
            lastPlaylistId = playlistId;
            addToPlaylist(panel, playlistId);
        } else {
            createPlaylist(panel);
        }

        return false;
    }

    function createPlaylist(dlg) {

        var url = ApiClient.getUrl("Playlists", {

            Name: $('#txtNewPlaylistName', dlg).val(),
            Ids: $('.fldSelectedItemIds', dlg).val() || '',
            userId: Dashboard.getCurrentUserId()

        });

        ApiClient.ajax({
            type: "POST",
            url: url,
            dataType: "json"

        }).done(function (result) {

            Dashboard.hideLoadingMsg();

            var id = result.Id;

            PaperDialogHelper.close(dlg);
            redirectToPlaylist(id);
        });
    }

    function addToPlaylist(dlg, id) {

        var url = ApiClient.getUrl("Playlists/" + id + "/Items", {

            Ids: $('.fldSelectedItemIds', dlg).val() || '',
            userId: Dashboard.getCurrentUserId()
        });

        ApiClient.ajax({
            type: "POST",
            url: url

        }).done(function () {

            Dashboard.hideLoadingMsg();

            PaperDialogHelper.close(dlg);
            Dashboard.alert(Globalize.translate('MessageAddedToPlaylistSuccess'));

        });
    }

    function onDialogClosed() {

        $(this).remove();
        Dashboard.hideLoadingMsg();
    }

    function populatePlaylists(panel) {

        var select = $('#selectPlaylistToAddTo', panel);

        if (!select.length) {

            $('#txtNewPlaylistName', panel).val('').focus();
            return;
        }

        Dashboard.showLoadingMsg();

        $('.newPlaylistInfo', panel).hide();

        var options = {

            Recursive: true,
            IncludeItemTypes: "Playlist",
            SortBy: 'SortName'
        };

        ApiClient.getItems(Dashboard.getCurrentUserId(), options).done(function (result) {

            var html = '';

            html += '<option value="">' + Globalize.translate('OptionNewPlaylist') + '</option>';

            html += result.Items.map(function (i) {

                return '<option value="' + i.Id + '">' + i.Name + '</option>';
            });

            select.html(html).val(lastPlaylistId || '').trigger('change');

            Dashboard.hideLoadingMsg();
        });
    }

    function getEditorHtml() {

        var html = '';

        html += '<form style="max-width:100%;">';

        html += '<br />';

        html += '<div class="fldSelectPlaylist">';
        html += '<br />';
        html += '<label for="selectPlaylistToAddTo">' + Globalize.translate('LabelSelectPlaylist') + '</label>';
        html += '<select id="selectPlaylistToAddTo" data-mini="true"></select>';
        html += '</div>';

        html += '<div class="newPlaylistInfo">';

        html += '<div>';
        html += '<paper-input type="text" id="txtNewPlaylistName" required="required" label="' + Globalize.translate('LabelName') + '"></paper-input>';
        html += '</div>';

        html += '<br />';

        // newPlaylistInfo
        html += '</div>';

        html += '<br />';
        html += '<div>';
        html += '<button type="submit" class="clearButton" data-role="none"><paper-button raised class="submit block">' + Globalize.translate('ButtonOk') + '</paper-button></button>';
        html += '</div>';

        html += '<input type="hidden" class="fldSelectedItemIds" />';

        html += '</form>';

        return html;
    }

    function initEditor(content, items) {

        $('#selectPlaylistToAddTo', content).on('change', function () {

            if (this.value) {
                $('.newPlaylistInfo', content).hide();
                $('input', content).removeAttr('required');
            } else {
                $('.newPlaylistInfo', content).show();
                $('input', content).attr('required', 'required');
            }

        }).trigger('change');

        populatePlaylists(content);

        $('form', content).on('submit', onAddToPlaylistFormSubmit);

        $('.fldSelectedItemIds', content).val(items.join(','));

        if (items.length) {
            $('.fldSelectPlaylist', content).show();
            populatePlaylists(content);
        } else {
            $('.fldSelectPlaylist', content).hide();
            $('#selectPlaylistToAddTo', content).html('').val('').trigger('change');
        }
    }

    function playlisteditor() {

        var self = this;

        self.show = function (items) {

            items = items || [];

            require(['components/paperdialoghelper'], function () {

                var dlg = PaperDialogHelper.createDialog({
                    size: 'small'
                });

                var html = '';
                html += '<h2 class="dialogHeader">';
                html += '<paper-fab icon="arrow-back" mini class="btnCloseDialog"></paper-fab>';

                var title = Globalize.translate('HeaderAddToPlaylist');

                html += '<div style="display:inline-block;margin-left:.6em;vertical-align:middle;">' + title + '</div>';
                html += '</h2>';

                html += '<div class="editorContent" style="max-width:800px;margin:auto;">';
                html += getEditorHtml();
                html += '</div>';

                dlg.innerHTML = html;
                document.body.appendChild(dlg);

                var editorContent = dlg.querySelector('.editorContent');
                initEditor(editorContent, items);

                $(dlg).on('iron-overlay-closed', onDialogClosed);

                PaperDialogHelper.openWithHash(dlg, 'playlisteditor');

                $('.btnCloseDialog', dlg).on('click', function () {

                    PaperDialogHelper.close(dlg);
                });
            });
        };
    }

    return playlisteditor;
});