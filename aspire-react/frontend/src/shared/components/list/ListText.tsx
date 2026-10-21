import { Tooltip, Typography } from 'antd';

type Value = string | null | undefined;

export function NowrapText({ value, code = false }: { value: Value; code?: boolean }) {
  const text = value || '-';
  return (
    <Typography.Text code={code} style={{ whiteSpace: 'nowrap' }}>
      {text}
    </Typography.Text>
  );
}

export function EllipsisText({ value }: { value: Value }) {
  const text = value || '-';
  return (
    <Tooltip title={text} placement="topLeft">
      <span style={{ display: 'block', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
        {text}
      </span>
    </Tooltip>
  );
}
